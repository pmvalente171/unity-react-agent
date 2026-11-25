"""Provider-neutral types shared by the agent and LLM layers.

Nothing in here knows about OpenAI, Anthropic or any other provider. Each LLM
subclass translates these types to and from its own wire format.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Literal


@dataclass(frozen=True)
class ToolSpec:
    """A tool the client (Unity) can execute."""

    name: str
    description: str = ""
    parameters: dict[str, Any] = field(
        default_factory=lambda: {"type": "object", "properties": {}}
    )


@dataclass(frozen=True)
class ToolCall:
    """A request from the model to run one tool."""

    id: str
    tool: str
    args: dict[str, Any] = field(default_factory=dict)


@dataclass(frozen=True)
class UserMessage:
    content: str
    role: Literal["user"] = "user"


@dataclass(frozen=True)
class AssistantMessage:
    """A previous agent step: a thought plus either an action or a final answer."""

    thought: str = ""
    action: ToolCall | None = None
    answer: str | None = None
    role: Literal["assistant"] = "assistant"


@dataclass(frozen=True)
class ObservationMessage:
    """The result of executing an action, sent back by the client."""

    action_id: str
    content: str
    is_error: bool = False  # the tool failed on the client side
    role: Literal["observation"] = "observation"


Message = UserMessage | AssistantMessage | ObservationMessage


@dataclass(frozen=True)
class LLMResponse:
    """What an LLM returned, already translated out of the provider format."""

    text: str = ""
    tool_calls: list[ToolCall] = field(default_factory=list)
    raw: Any = None  # provider-specific response, for logging/debugging only


@dataclass(frozen=True)
class StepResult:
    """One ReAct step, as returned to the client."""

    type: Literal["action", "final"]
    thought: str = ""
    action: ToolCall | None = None
    answer: str | None = None

    def to_dict(self) -> dict[str, Any]:
        if self.type == "action":
            assert self.action is not None
            return {
                "type": "action",
                "thought": self.thought,
                "action": {
                    "id": self.action.id,
                    "tool": self.action.tool,
                    "args": self.action.args,
                },
            }
        return {"type": "final", "thought": self.thought, "answer": self.answer or ""}
