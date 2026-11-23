"""One ReAct step: everything agent-related, nothing provider-related.

Unity owns the loop and the history. The agent receives the full history,
asks the LLM for the next step, checks the reply makes sense, and returns
either an action (one tool call) or a final answer.
"""

from __future__ import annotations

import uuid
from dataclasses import replace

import jsonschema

from core.types import (
    AssistantMessage,
    LLMResponse,
    Message,
    ObservationMessage,
    StepResult,
    ToolCall,
    ToolSpec,
    UserMessage,
)
from llm.base import LLM

DEFAULT_SYSTEM_PROMPT = (
    "You are an agent acting inside a game world. Work step by step. "
    "On every turn, first write a short explanation of your reasoning in plain text "
    "(this is shown to the player), then either call exactly one tool, or, if the task "
    "is done, give your final answer as plain text without calling a tool."
)


class AgentError(Exception):
    """The model kept producing a reply the agent cannot turn into a valid step."""


class ReActAgent:
    def __init__(self, llm: LLM, system_prompt: str = DEFAULT_SYSTEM_PROMPT, max_retries: int = 1):
        self.llm = llm
        self.system_prompt = system_prompt
        self.max_retries = max_retries

    def step(self, messages: list[Message], tools: list[ToolSpec]) -> StepResult:
        if not self.llm.card.supports_native_tools:
            # Planned: text-based ReAct (Thought/Action/Action Input) for models
            # without native tool calling, e.g. a vLLM server without a tool parser.
            raise NotImplementedError(
                f"model {self.llm.card.name!r} has no native tool calling; "
                "the text-ReAct fallback is not implemented yet"
            )

        tools_by_name = {t.name: t for t in tools}
        history = list(messages)
        problem = ""
        for _ in range(self.max_retries + 1):
            reply = self._ensure_unique_ids(self.llm.generate(self.system_prompt, history, tools), history)
            result, problem, feedback = self._interpret(reply, tools_by_name)
            if result is not None:
                return result
            # Show the model its mistake and let it try again. This correction
            # only lives for this request; Unity's history is not changed.
            history.extend(feedback)
        raise AgentError(f"model gave no valid step after {self.max_retries + 1} attempts: {problem}")

    @staticmethod
    def _ensure_unique_ids(reply: LLMResponse, history: list[Message]) -> LLMResponse:
        """Guarantee every action id is non-empty and unused in this conversation.

        Unity matches observations to actions by id, and some providers (e.g.
        vLLM) may return no id or reuse one, so the server fills in its own.
        """
        used = {m.action.id for m in history if isinstance(m, AssistantMessage) and m.action}
        used |= {m.action_id for m in history if isinstance(m, ObservationMessage)}
        calls = []
        for c in reply.tool_calls:
            if not c.id or c.id in used:
                c = replace(c, id=f"act_{uuid.uuid4().hex}")
            used.add(c.id)
            calls.append(c)
        return replace(reply, tool_calls=calls)

    def _interpret(
        self, reply: LLMResponse, tools_by_name: dict[str, ToolSpec]
    ) -> tuple[StepResult | None, str, list[Message]]:
        """Returns (result, "", []) on success, or (None, problem, feedback messages) to retry."""
        thought = reply.text.strip()

        if not reply.tool_calls:
            if not thought:
                # No tool call to attach an observation to, so nudge as the user.
                return None, "empty reply", [UserMessage(content=(
                    "Your last reply was empty. Explain your reasoning, "
                    "then call a tool or give your final answer."
                ))]
            return StepResult(type="final", thought="", answer=thought), "", []

        call = reply.tool_calls[0]  # one action per step; extra calls are ignored
        error = self._validate_call(call, tools_by_name)
        if error is None:
            return StepResult(type="action", thought=thought, action=call), "", []
        return None, error, [
            AssistantMessage(thought=thought, action=call),
            ObservationMessage(action_id=call.id, content=f"Error: {error} No action was taken."),
        ]

    @staticmethod
    def _validate_call(call: ToolCall, tools_by_name: dict[str, ToolSpec]) -> str | None:
        spec = tools_by_name.get(call.tool)
        if spec is None:
            available = ", ".join(sorted(tools_by_name)) or "none"
            return f"unknown tool {call.tool!r}. Available tools: {available}."
        if "_raw" in call.args:
            return f"the arguments for {call.tool!r} were not a valid JSON object."
        try:
            jsonschema.validate(call.args, spec.parameters)
        except jsonschema.ValidationError as e:
            return f"invalid arguments for {call.tool!r}: {e.message}."
        except jsonschema.SchemaError:
            pass  # the client sent a broken schema; don't blame the model for it
        return None

