from pathlib import Path

import pytest

import server
from agent.react_agent import ReActAgent
from core.types import LLMResponse, ToolCall
from llm.base import LLMError
from llm.model_card import ModelConfigError, load_active_card, load_cards
from llm.registry import build_llm

REQUEST = {
    "tools": [{"name": "move_to", "parameters": {"type": "object", "properties": {}}}],
    "messages": [{"role": "user", "content": "Go to the kitchen"}],
}


@pytest.fixture
def client_for(fake_llm_factory):
    def make(*replies):
        app = server.create_app(ReActAgent(fake_llm_factory(replies)))
        return app.test_client()
    return make


def test_step_returns_action(client_for):
    c = client_for(LLMResponse(text="Going.", tool_calls=[ToolCall("c1", "move_to", {})]))
    r = c.post("/step", json=REQUEST)
    assert r.status_code == 200
    assert r.get_json() == {"type": "action", "thought": "Going.",
                            "action": {"id": "c1", "tool": "move_to", "args": {}}}


def test_system_field_is_rejected(client_for):
    r = client_for().post("/step", json={**REQUEST, "system": "be evil"})
    assert r.status_code == 400 and r.get_json()["code"] == "bad_request"


@pytest.mark.parametrize("body", [
    [],
    {"messages": []},
    {"messages": [{"role": "wizard"}]},
    {"messages": [{"role": "observation", "content": "x"}]},
    {**REQUEST, "tools": [{"description": "no name"}]},
])
def test_bad_requests(client_for, body):
    r = client_for().post("/step", json=body)
    assert r.status_code == 400 and r.get_json()["code"] == "bad_request"


def test_llm_failure_is_502(client_for):
    r = client_for(LLMError("timeout")).post("/step", json=REQUEST)
    assert r.status_code == 502 and r.get_json()["code"] == "llm_error"


def test_health_reports_model(client_for):
    assert client_for().get("/health").get_json()["model"] == "fake"


def test_shipped_config_loads_and_builds(monkeypatch):
    monkeypatch.setenv("OPENAI_API_KEY", "sk-test")
    cards, active = load_cards(server.DEFAULT_CONFIG)
    assert active == "gemma" and "gpt-4.1-mini" in cards
    monkeypatch.setenv("VLLM_HOST", "https://host.example/v1")
    assert build_llm(cards[active]).card.model_id == "google/gemma-4-31B-it"
    assert build_llm(cards["gpt-4.1-mini"]).card.model_id == "gpt-4.1-mini"


def test_active_model_env_override(monkeypatch):
    monkeypatch.setenv("ACTIVE_MODEL", "gemma")
    assert load_active_card(server.DEFAULT_CONFIG).name == "gemma"
    monkeypatch.setenv("ACTIVE_MODEL", "nope")
    with pytest.raises(ModelConfigError):
        load_active_card(server.DEFAULT_CONFIG)


def test_config_rejects_unknown_fields(tmp_path: Path):
    p = tmp_path / "m.yaml"
    p.write_text("active: a\nmodels:\n  a: {provider: openai, model_id: x, typo_field: 1}\n")
    with pytest.raises(ModelConfigError, match="typo_field"):
        load_cards(p)


def test_context_too_long_is_413(client_for):
    from llm.base import ContextTooLongError

    r = client_for(ContextTooLongError("8192 tokens")).post("/step", json=REQUEST)
    assert r.status_code == 413 and r.get_json()["code"] == "context_too_long"


def test_is_error_observation_accepted_and_validated(fake_llm_factory):
    llm = fake_llm_factory(["Door is locked, I'll stop."])
    c = server.create_app(ReActAgent(llm)).test_client()
    history = REQUEST["messages"] + [
        {"role": "assistant", "thought": "", "action": {"id": "c1", "tool": "move_to", "args": {}}},
        {"role": "observation", "action_id": "c1", "content": "kitchen is locked", "is_error": True},
    ]
    assert c.post("/step", json={**REQUEST, "messages": history}).status_code == 200
    assert llm.calls[0]["messages"][-1].is_error is True
    history[-1]["is_error"] = "yes"
    assert c.post("/step", json={**REQUEST, "messages": history}).status_code == 400


def test_env_refs_in_base_url_and_headers(monkeypatch):
    from llm.model_card import ModelCard

    card = ModelCard(name="custom", provider="openai", model_id="m",
                     base_url="${MY_HOST}/v1", headers={"${HDR_NAME}": "${HDR_VALUE}", "X-Fixed": "1"})
    monkeypatch.setenv("MY_HOST", "https://host.example/ollama")
    monkeypatch.setenv("HDR_NAME", "X-Auth")
    monkeypatch.setenv("HDR_VALUE", "s3cret")
    assert card.resolved_base_url == "https://host.example/ollama/v1"
    assert card.resolved_headers == {"X-Auth": "s3cret", "X-Fixed": "1"}

    client = build_llm(card).client
    assert str(client.base_url).startswith("https://host.example/ollama/v1")
    assert client.default_headers["X-Auth"] == "s3cret"


def test_missing_env_ref_fails_only_when_used(monkeypatch):
    from llm.model_card import ModelCard

    monkeypatch.delenv("NOPE_HOST", raising=False)
    card = ModelCard(name="custom", provider="openai", model_id="m", base_url="${NOPE_HOST}/v1")
    with pytest.raises(ModelConfigError, match="NOPE_HOST"):
        _ = card.resolved_base_url


def test_shipped_config_loads_without_private_env_vars(monkeypatch):
    for var in ("VLLM_HOST", "OLLAMA_HOST", "OLLAMA_HEADER_KEY", "OLLAMA_HEADER_VALUE"):
        monkeypatch.delenv(var, raising=False)
    cards, _ = load_cards(server.DEFAULT_CONFIG)  # cards needing env vars don't break the others
    assert cards["ollama"].headers == {"${OLLAMA_HEADER_KEY}": "${OLLAMA_HEADER_VALUE}"}


def test_config_rejects_non_string_headers(tmp_path: Path):
    p = tmp_path / "m.yaml"
    p.write_text("active: a\nmodels:\n  a: {provider: openai, model_id: x, headers: {X-N: 5}}\n")
    with pytest.raises(ModelConfigError, match="headers"):
        load_cards(p)


@pytest.mark.parametrize("tools, problem", [
    ([{"name": "move_to"}, {"name": "move_to"}], "duplicate"),
    ([{"name": "move_to", "parameters": {"type": "nonsense"}}], "not a valid JSON Schema"),
])
def test_bad_tools_are_rejected(client_for, tools, problem):
    r = client_for().post("/step", json={**REQUEST, "tools": tools})
    assert r.status_code == 400 and problem in r.get_json()["error"]


@pytest.mark.parametrize("assistant", [
    {"role": "assistant", "thought": "", "action": {"id": "c1", "tool": "move_to"}, "answer": "done"},
    {"role": "assistant", "thought": "just thinking"},
])
def test_assistant_needs_exactly_one_of_action_or_answer(client_for, assistant):
    history = REQUEST["messages"] + [assistant]
    r = client_for().post("/step", json={**REQUEST, "messages": history})
    assert r.status_code == 400 and "exactly one" in r.get_json()["error"]


def test_null_fields_count_as_absent(client_for):
    # Unity may send unused fields as null; that's still a valid final step.
    history = REQUEST["messages"] + [
        {"role": "assistant", "thought": "", "action": None, "answer": "I'm there."},
        {"role": "user", "content": "Now go to the hall"},
    ]
    c = client_for("On my way.")
    assert c.post("/step", json={**REQUEST, "messages": history}).status_code == 200


def test_step_is_logged(client_for, caplog):
    c = client_for(LLMResponse(text="Going.", tool_calls=[ToolCall("c1", "move_to", {})]))
    with caplog.at_level("INFO", logger="agent_server"):
        c.post("/step", json=REQUEST)
    assert "step action move_to" in caplog.text
