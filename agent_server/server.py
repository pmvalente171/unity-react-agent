"""Stateless ReAct step server.

Unity owns the loop and the history. On every step it POSTs the full history
plus the tools it can execute; the server returns exactly one next step.
See CONTRACT.md for the wire format.

This module only does HTTP: JSON in -> neutral types -> agent.step() -> JSON out.
Agent logic lives in agent/, provider logic in llm/, models in config/models.yaml.
"""

from __future__ import annotations

import logging
import os
import time
from pathlib import Path
from typing import Any

import jsonschema
from flask import Flask, jsonify, request

from agent.react_agent import AgentError, ReActAgent
from core.types import (
    AssistantMessage,
    Message,
    ObservationMessage,
    ToolCall,
    ToolSpec,
    UserMessage,
)
from llm.base import ContextTooLongError, LLMError
from llm.model_card import load_active_card
from llm.registry import build_llm

DEFAULT_CONFIG = Path(__file__).parent / "config" / "models.yaml"

log = logging.getLogger("agent_server")


class BadRequest(Exception):
    pass


# ---- JSON -> neutral types ----

def parse_tools(raw: Any) -> list[ToolSpec]:
    if raw is None:
        return []
    if not isinstance(raw, list):
        raise BadRequest("'tools' must be a list")
    tools = []
    seen: set[str] = set()
    for i, t in enumerate(raw):
        if not isinstance(t, dict) or not isinstance(t.get("name"), str) or not t["name"]:
            raise BadRequest(f"tools[{i}]: needs a non-empty string 'name'")
        name = t["name"]
        if name in seen:
            raise BadRequest(f"tools[{i}]: duplicate tool name {name!r}")
        seen.add(name)
        params = t.get("parameters") or {"type": "object", "properties": {}}
        if not isinstance(params, dict):
            raise BadRequest(f"tools[{i}]: 'parameters' must be a JSON Schema object")
        try:
            jsonschema.validators.validator_for(params).check_schema(params)
        except jsonschema.SchemaError as e:
            raise BadRequest(f"tools[{i}] ({name!r}): 'parameters' is not a valid JSON Schema: {e.message}") from None
        tools.append(ToolSpec(name=name, description=str(t.get("description", "")), parameters=params))
    return tools


def parse_messages(raw: Any) -> list[Message]:
    if not isinstance(raw, list) or not raw:
        raise BadRequest("'messages' must be a non-empty list")
    out: list[Message] = []
    for i, m in enumerate(raw):
        if not isinstance(m, dict):
            raise BadRequest(f"messages[{i}]: must be an object")
        role = m.get("role")
        if role == "user":
            out.append(UserMessage(content=str(m.get("content", ""))))
        elif role == "assistant":
            out.append(_parse_assistant(i, m))
        elif role == "observation":
            if not m.get("action_id"):
                raise BadRequest(f"messages[{i}]: observation needs 'action_id'")
            is_error = m.get("is_error", False)
            if not isinstance(is_error, bool):
                raise BadRequest(f"messages[{i}]: 'is_error' must be true or false")
            out.append(ObservationMessage(
                action_id=str(m["action_id"]), content=str(m.get("content", "")), is_error=is_error,
            ))
        else:
            raise BadRequest(f"messages[{i}]: unknown role {role!r}")
    return out


def _parse_assistant(i: int, m: dict[str, Any]) -> AssistantMessage:
    action, answer = m.get("action"), m.get("answer")
    if (action is None) == (answer is None):
        raise BadRequest(f"messages[{i}]: an assistant message needs exactly one of 'action' or 'answer'")
    call = None
    if action is not None:
        if not isinstance(action, dict) or not action.get("id") or not action.get("tool"):
            raise BadRequest(f"messages[{i}]: 'action' needs 'id' and 'tool'")
        args = action.get("args") or {}
        if not isinstance(args, dict):
            raise BadRequest(f"messages[{i}]: 'action.args' must be an object")
        call = ToolCall(id=str(action["id"]), tool=str(action["tool"]), args=args)
    return AssistantMessage(
        thought=str(m.get("thought") or ""),
        action=call,
        answer=None if answer is None else str(answer),
    )


# ---- app ----

def error(status: int, code: str, message: str):
    log.warning("step failed: %s %s: %s", status, code, message)
    return jsonify({"error": message, "code": code}), status


def create_app(agent: ReActAgent) -> Flask:
    app = Flask(__name__)

    @app.get("/health")
    def health():
        card = agent.llm.card
        return jsonify({"status": "ok", "model": card.name, "provider": card.provider})

    @app.post("/step")
    def step():
        body = request.get_json(silent=True)
        if not isinstance(body, dict):
            return error(400, "bad_request", "expected a JSON object")
        if "system" in body:
            return error(400, "bad_request", "'system' is not accepted; the system prompt is set on the server")

        instructions = body.get("instructions")
        if instructions is None:
            instructions = ""
        elif not isinstance(instructions, str):
            return error(400, "bad_request", "'instructions' must be a string")

        try:
            messages = parse_messages(body.get("messages"))
            tools = parse_tools(body.get("tools"))
        except BadRequest as e:
            return error(400, "bad_request", str(e))

        started = time.perf_counter()
        try:
            result = agent.step(messages, tools, instructions)
        except ContextTooLongError as e:
            return error(413, "context_too_long", f"history does not fit the model's context window: {e}")
        except LLMError as e:
            return error(502, "llm_error", f"LLM call failed: {e}")
        except AgentError as e:
            return error(502, "agent_error", str(e))
        except NotImplementedError as e:
            return error(501, "not_supported", str(e))

        log.info(
            "step %s%s (%d messages, %d tools, %.2fs)",
            result.type, f" {result.action.tool}" if result.action else "",
            len(messages), len(tools), time.perf_counter() - started,
        )
        log.debug("step result: %s", result.to_dict())
        return jsonify(result.to_dict())

    return app


def create_default_app() -> Flask:
    """App for the active model in the config. gunicorn runs it as 'server:create_default_app()'."""
    logging.basicConfig(
        level=os.environ.get("LOG_LEVEL", "INFO").upper(),
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )
    card = load_active_card(os.environ.get("MODEL_CONFIG", DEFAULT_CONFIG))
    log.info("active model: %s (%s, %s)", card.name, card.provider, card.model_id)
    return create_app(ReActAgent(build_llm(card)))


if __name__ == "__main__":
    # Local development only; Docker runs gunicorn. Listens on localhost unless HOST says
    # otherwise, and never turns on Flask's debugger, which would let anyone who can reach
    # the port run code on this machine.
    create_default_app().run(host=os.environ.get("HOST", "127.0.0.1"), port=int(os.environ.get("PORT", 5000)))
