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
    assert active == "gpt-4.1-mini" and "lab-gemma" in cards
    assert build_llm(cards[active]).card.model_id == "gpt-4.1-mini"


def test_active_model_env_override(monkeypatch):
    monkeypatch.setenv("ACTIVE_MODEL", "lab-gemma")
    assert load_active_card(server.DEFAULT_CONFIG).name == "lab-gemma"
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
