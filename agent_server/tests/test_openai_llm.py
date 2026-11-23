from types import SimpleNamespace

import pytest
from openai import APIConnectionError

from core.types import AssistantMessage, ObservationMessage, ToolCall, ToolSpec, UserMessage
from llm.base import LLMError
from llm.model_card import ModelCard
from llm.openai_llm import OpenAILLM

CARD = ModelCard(name="gpt", provider="openai", model_id="gpt-x", supports_parallel_tools=True)
HISTORY = [
    UserMessage("Go to the kitchen"),
    AssistantMessage(thought="Walking.", action=ToolCall("call_1", "move_to", {"place": "kitchen"})),
    ObservationMessage("call_1", "Arrived"),
    AssistantMessage(thought="Arrived.", answer="I'm in the kitchen."),
]
TOOLS = [ToolSpec(name="move_to", description="Move somewhere")]


class FakeClient:
    def __init__(self, message=None, error=None):
        self.kwargs = None
        self._message, self._error = message, error
        self.chat = SimpleNamespace(completions=SimpleNamespace(create=self._create))

    def _create(self, **kwargs):
        self.kwargs = kwargs
        if self._error:
            raise self._error
        return SimpleNamespace(choices=[SimpleNamespace(message=self._message)])


def tool_call(id, name, arguments):
    return SimpleNamespace(id=id, function=SimpleNamespace(name=name, arguments=arguments))


def test_history_is_translated_to_openai_format():
    llm = OpenAILLM(CARD, client=FakeClient())
    assert llm.to_provider_messages("SYS", HISTORY) == [
        {"role": "system", "content": "SYS"},
        {"role": "user", "content": "Go to the kitchen"},
        {"role": "assistant", "content": "Walking.", "tool_calls": [{
            "id": "call_1", "type": "function",
            "function": {"name": "move_to", "arguments": '{"place": "kitchen"}'},
        }]},
        {"role": "tool", "tool_call_id": "call_1", "content": "Arrived"},
        {"role": "assistant", "content": "Arrived.\n\nI'm in the kitchen."},
    ]


def test_no_system_role_merges_prompt_into_first_user_message():
    card = ModelCard(name="gemma", provider="openai", model_id="g", supports_system_role=False)
    out = OpenAILLM(card, client=FakeClient()).to_provider_messages("SYS", HISTORY)
    assert out[0] == {"role": "user", "content": "SYS\n\nGo to the kitchen"}
    assert all(m["role"] != "system" for m in out)


def test_generate_sends_tools_and_parses_reply():
    client = FakeClient(SimpleNamespace(
        content="Going.", tool_calls=[tool_call("c9", "move_to", '{"place": "hall"}')]
    ))
    reply = OpenAILLM(CARD, client=client).generate("SYS", HISTORY[:1], TOOLS)
    assert client.kwargs["model"] == "gpt-x"
    assert client.kwargs["tools"][0]["function"]["name"] == "move_to"
    assert client.kwargs["parallel_tool_calls"] is False
    assert reply.text == "Going."
    assert reply.tool_calls == [ToolCall("c9", "move_to", {"place": "hall"})]


def test_parallel_flag_not_sent_when_model_lacks_it():
    card = ModelCard(name="m", provider="openai", model_id="m", supports_parallel_tools=False)
    client = FakeClient(SimpleNamespace(content="ok", tool_calls=None))
    OpenAILLM(card, client=client).generate("SYS", HISTORY[:1], TOOLS)
    assert "parallel_tool_calls" not in client.kwargs


def test_bad_json_arguments_are_kept_raw():
    client = FakeClient(SimpleNamespace(content="", tool_calls=[tool_call("c1", "move_to", "{oops")]))
    reply = OpenAILLM(CARD, client=client).generate("SYS", HISTORY[:1], TOOLS)
    assert reply.tool_calls[0].args == {"_raw": "{oops"}


def test_provider_errors_become_llm_error():
    err = APIConnectionError(request=SimpleNamespace(method="POST", url="x"))
    with pytest.raises(LLMError):
        OpenAILLM(CARD, client=FakeClient(error=err)).generate("SYS", HISTORY[:1], TOOLS)


def test_error_observation_is_marked_for_the_model():
    llm = OpenAILLM(CARD, client=FakeClient())
    out = llm.to_provider_messages("", [
        ObservationMessage("a", "kitchen is locked", is_error=True),
        ObservationMessage("b", "Error: already said", is_error=True),
    ])
    assert [m["content"] for m in out] == ["Error: kitchen is locked", "Error: already said"]


@pytest.mark.parametrize("code,message", [
    ("context_length_exceeded", "too long"),
    (None, "This model's maximum context length is 8192 tokens"),
])
def test_context_overflow_becomes_context_too_long(code, message):
    from openai import BadRequestError

    from llm.base import ContextTooLongError

    resp = fake_http_response(400)
    err = BadRequestError(message, response=resp, body={"code": code, "message": message})
    with pytest.raises(ContextTooLongError):
        OpenAILLM(CARD, client=FakeClient(error=err)).generate("SYS", HISTORY[:1], TOOLS)


def test_other_bad_requests_stay_llm_error():
    from openai import BadRequestError

    from llm.base import ContextTooLongError

    resp = fake_http_response(400)
    err = BadRequestError("invalid tool schema", response=resp, body={"code": "invalid_value"})
    with pytest.raises(LLMError) as info:
        OpenAILLM(CARD, client=FakeClient(error=err)).generate("SYS", HISTORY[:1], TOOLS)
    assert not isinstance(info.value, ContextTooLongError)


def fake_http_response(status):
    return SimpleNamespace(
        status_code=status, headers={}, request=SimpleNamespace(method="POST", url="http://x")
    )
