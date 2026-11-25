import pytest

from agent.react_agent import AgentError, ReActAgent
from core.types import LLMResponse, ObservationMessage, ToolCall, ToolSpec, UserMessage
from llm.model_card import ModelCard

MOVE = ToolSpec(
    name="move_to",
    parameters={
        "type": "object",
        "properties": {"place": {"type": "string"}},
        "required": ["place"],
    },
)
HISTORY = [UserMessage("Go to the kitchen")]


def call(tool="move_to", args=None, id="call_1"):
    return ToolCall(id=id, tool=tool, args={"place": "kitchen"} if args is None else args)


def test_valid_tool_call_becomes_action(fake_llm_factory):
    llm = fake_llm_factory([LLMResponse(text="I'll walk there.", tool_calls=[call()])])
    result = ReActAgent(llm).step(HISTORY, [MOVE])
    assert result.to_dict() == {
        "type": "action",
        "thought": "I'll walk there.",
        "action": {"id": "call_1", "tool": "move_to", "args": {"place": "kitchen"}},
    }


def test_plain_text_becomes_final_answer(fake_llm_factory):
    llm = fake_llm_factory(["I'm in the kitchen."])
    assert ReActAgent(llm).step(HISTORY, [MOVE]).to_dict() == {
        "type": "final", "thought": "", "answer": "I'm in the kitchen.",
    }


def test_only_first_of_several_tool_calls_is_used(fake_llm_factory):
    llm = fake_llm_factory([LLMResponse(tool_calls=[call(id="a"), call(id="b")])])
    assert ReActAgent(llm).step(HISTORY, [MOVE]).action.id == "a"


def test_unknown_tool_is_retried_with_feedback(fake_llm_factory):
    llm = fake_llm_factory([
        LLMResponse(tool_calls=[call(tool="fly_to", id="bad")]),
        LLMResponse(tool_calls=[call()]),
    ])
    result = ReActAgent(llm).step(HISTORY, [MOVE])
    assert result.action.tool == "move_to"
    feedback = llm.calls[1]["messages"][-1]
    assert isinstance(feedback, ObservationMessage)
    assert feedback.action_id == "bad" and "unknown tool 'fly_to'" in feedback.content
    assert len(HISTORY) == 1  # the caller's history is never modified


def test_schema_violation_is_retried(fake_llm_factory):
    llm = fake_llm_factory([
        LLMResponse(tool_calls=[call(args={})]),
        LLMResponse(tool_calls=[call()]),
    ])
    ReActAgent(llm).step(HISTORY, [MOVE])
    assert "'place' is a required property" in llm.calls[1]["messages"][-1].content


def test_unparseable_arguments_are_retried(fake_llm_factory):
    llm = fake_llm_factory([
        LLMResponse(tool_calls=[call(args={"_raw": "{not json"})]),
        LLMResponse(tool_calls=[call()]),
    ])
    assert ReActAgent(llm).step(HISTORY, [MOVE]).type == "action"


def test_empty_reply_is_retried_with_nudge(fake_llm_factory):
    llm = fake_llm_factory(["", "Done."])
    assert ReActAgent(llm).step(HISTORY, [MOVE]).answer == "Done."
    assert isinstance(llm.calls[1]["messages"][-1], UserMessage)


def test_gives_up_after_retries(fake_llm_factory):
    llm = fake_llm_factory([LLMResponse(tool_calls=[call(tool="nope")])] * 2)
    with pytest.raises(AgentError, match="unknown tool 'nope'"):
        ReActAgent(llm).step(HISTORY, [MOVE])


def test_model_without_native_tools_is_not_supported_yet(fake_llm_factory):
    card = ModelCard(name="gemma", provider="openai", model_id="g", supports_native_tools=False)
    with pytest.raises(NotImplementedError, match="text-ReAct"):
        ReActAgent(fake_llm_factory([], card=card)).step(HISTORY, [MOVE])


@pytest.mark.parametrize("bad_id", ["", "call_1"])
def test_missing_or_reused_action_id_is_replaced(fake_llm_factory, bad_id):
    from core.types import AssistantMessage

    history = [
        UserMessage("Go"),
        AssistantMessage(thought="", action=call(id="call_1")),
        ObservationMessage("call_1", "Arrived"),
    ]
    llm = fake_llm_factory([LLMResponse(tool_calls=[call(id=bad_id)])])
    action_id = ReActAgent(llm).step(history, [MOVE]).action.id
    assert action_id.startswith("act_") and len(action_id) > 10


def test_provider_id_kept_when_unique(fake_llm_factory):
    llm = fake_llm_factory([LLMResponse(tool_calls=[call(id="call_new")])])
    assert ReActAgent(llm).step(HISTORY, [MOVE]).action.id == "call_new"
