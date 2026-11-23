"""OpenAI Chat Completions, and any server that speaks the same API (vLLM, Ollama, ...)."""

from __future__ import annotations

import json
from typing import Any

from openai import BadRequestError, OpenAI, OpenAIError

from core.types import (
    AssistantMessage,
    LLMResponse,
    Message,
    ObservationMessage,
    ToolCall,
    ToolSpec,
    UserMessage,
)
from llm.base import LLM, ContextTooLongError, LLMError
from llm.model_card import ModelCard


def _is_context_overflow(e: BadRequestError) -> bool:
    # OpenAI sends code "context_length_exceeded"; vLLM only says it in the message.
    if getattr(e, "code", None) == "context_length_exceeded":
        return True
    text = str(e).lower()
    return "context length" in text or "context window" in text


class OpenAILLM(LLM):
    def __init__(self, card: ModelCard, client: OpenAI | None = None):
        super().__init__(card)
        if client is None:
            # OpenAI-compatible servers often need no key, but the SDK insists on one.
            api_key = card.api_key or ("not-needed" if card.base_url else None)
            client = OpenAI(api_key=api_key, base_url=card.base_url)
        self.client = client

    def generate(
        self, system: str, messages: list[Message], tools: list[ToolSpec]
    ) -> LLMResponse:
        kwargs: dict[str, Any] = {
            "model": self.card.model_id,
            "messages": self.to_provider_messages(system, messages),
        }
        if tools and self.card.supports_native_tools:
            kwargs["tools"] = self.to_provider_tools(tools)
            if self.card.supports_parallel_tools:
                kwargs["parallel_tool_calls"] = False  # one action per ReAct step

        try:
            completion = self.client.chat.completions.create(**kwargs)
        except BadRequestError as e:
            if _is_context_overflow(e):
                raise ContextTooLongError(f"{self.card.name}: {e}") from e
            raise LLMError(f"{self.card.name}: {e}") from e
        except OpenAIError as e:
            raise LLMError(f"{self.card.name}: {e}") from e

        if not completion.choices:
            raise LLMError(f"{self.card.name}: response had no choices")
        reply = completion.choices[0].message
        return LLMResponse(
            text=reply.content or "",
            tool_calls=[self._parse_tool_call(c) for c in (reply.tool_calls or [])],
            raw=completion,
        )

    # ---- translation: neutral types -> OpenAI ----

    @staticmethod
    def to_provider_tools(tools: list[ToolSpec]) -> list[dict[str, Any]]:
        return [
            {
                "type": "function",
                "function": {
                    "name": t.name,
                    "description": t.description,
                    "parameters": t.parameters,
                },
            }
            for t in tools
        ]

    def to_provider_messages(self, system: str, messages: list[Message]) -> list[dict[str, Any]]:
        out: list[dict[str, Any]] = []
        for m in messages:
            if isinstance(m, UserMessage):
                out.append({"role": "user", "content": m.content})
            elif isinstance(m, AssistantMessage):
                msg: dict[str, Any] = {"role": "assistant", "content": m.thought}
                if m.action:
                    msg["tool_calls"] = [{
                        "id": m.action.id,
                        "type": "function",
                        "function": {"name": m.action.tool, "arguments": json.dumps(m.action.args)},
                    }]
                elif m.answer:
                    msg["content"] = f"{m.thought}\n\n{m.answer}" if m.thought else m.answer
                out.append(msg)
            elif isinstance(m, ObservationMessage):
                # The OpenAI API has no error flag on tool results, so say it in the text.
                content = m.content
                if m.is_error and not content.lower().startswith("error"):
                    content = f"Error: {content}"
                out.append({"role": "tool", "tool_call_id": m.action_id, "content": content})
            else:  # pragma: no cover - guarded by the type
                raise TypeError(f"unknown message type {type(m).__name__}")

        if not system:
            return out
        if self.card.supports_system_role:
            return [{"role": "system", "content": system}, *out]
        # No system role (e.g. Gemma): prepend the prompt to the first user message.
        for msg in out:
            if msg["role"] == "user":
                msg["content"] = f"{system}\n\n{msg['content']}"
                return out
        return [{"role": "user", "content": system}, *out]

    # ---- translation: OpenAI -> neutral types ----

    @staticmethod
    def _parse_tool_call(call: Any) -> ToolCall:
        try:
            args = json.loads(call.function.arguments or "{}")
        except json.JSONDecodeError:
            args = {"_raw": call.function.arguments}
        if not isinstance(args, dict):
            args = {"_raw": args}
        return ToolCall(id=call.id, tool=call.function.name, args=args)
