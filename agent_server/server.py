"""Stateless ReAct step server.

Unity owns the loop and the history. On every step it POSTs the full history
plus the tools it can execute; the server returns exactly one next step.
See CONTRACT.md for the wire format.

This module only does HTTP: JSON in -> neutral types -> agent.step() -> JSON out.
Agent logic lives in agent/, provider logic in llm/, models in config/models.yaml.
"""

from __future__ import annotations

import os
from pathlib import Path
from typing import Any

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


class BadRequest(Exception):
    pass


# ---- JSON -> neutral types ----

def parse_tools(raw: Any) -> list[ToolSpec]:
    if raw is None:
        return []
    if not isinstance(raw, list):
        raise BadRequest("'tools' must be a list")
    tools = []
    for i, t in enumerate(raw):
        if not isinstance(t, dict) or not isinstance(t.get("name"), str) or not t["name"]:
            raise BadRequest(f"tools[{i}]: needs a non-empty string 'name'")
        params = t.get("parameters") or {"type": "object", "properties": {}}
        if not isinstance(params, dict):
            raise BadRequest(f"tools[{i}]: 'parameters' must be a JSON Schema object")
        tools.append(ToolSpec(name=t["name"], description=str(t.get("description", "")), parameters=params))
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
            action = m.get("action")
            call = None
            if action is not None:
                if not isinstance(action, dict) or not action.get("id") or not action.get("tool"):
                    raise BadRequest(f"messages[{i}]: 'action' needs 'id' and 'tool'")
                args = action.get("args") or {}
                if not isinstance(args, dict):
                    raise BadRequest(f"messages[{i}]: 'action.args' must be an object")
                call = ToolCall(id=str(action["id"]), tool=str(action["tool"]), args=args)
            answer = m.get("answer")
            out.append(AssistantMessage(
                thought=str(m.get("thought") or ""),
                action=call,
                answer=None if answer is None else str(answer),
            ))
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


# ---- app ----

def error(status: int, code: str, message: str):
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

        try:
            messages = parse_messages(body.get("messages"))
            tools = parse_tools(body.get("tools"))
        except BadRequest as e:
            return error(400, "bad_request", str(e))

        try:
            result = agent.step(messages, tools)
        except ContextTooLongError as e:
            return error(413, "context_too_long", f"history does not fit the model's context window: {e}")
        except LLMError as e:
            return error(502, "llm_error", f"LLM call failed: {e}")
        except AgentError as e:
            return error(502, "agent_error", str(e))
        except NotImplementedError as e:
            return error(501, "not_supported", str(e))
        return jsonify(result.to_dict())

    return app


def create_default_app() -> Flask:
    """App for the active model in the config. gunicorn runs it as 'server:create_default_app()'."""
    card = load_active_card(os.environ.get("MODEL_CONFIG", DEFAULT_CONFIG))
    return create_app(ReActAgent(build_llm(card)))


if __name__ == "__main__":
    create_default_app().run(host="0.0.0.0", port=int(os.environ.get("PORT", 5000)), debug=True)
