import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from core.types import LLMResponse  # noqa: E402
from llm.base import LLM  # noqa: E402
from llm.model_card import ModelCard  # noqa: E402


class FakeLLM(LLM):
    """Returns scripted replies and records what it was asked."""

    def __init__(self, replies, card=None):
        super().__init__(card or ModelCard(name="fake", provider="fake", model_id="fake"))
        self.replies = list(replies)
        self.calls = []

    def generate(self, system, messages, tools):
        self.calls.append({"system": system, "messages": list(messages), "tools": list(tools)})
        reply = self.replies.pop(0)
        if isinstance(reply, Exception):
            raise reply
        return reply if isinstance(reply, LLMResponse) else LLMResponse(text=reply)


@pytest.fixture
def fake_llm_factory():
    return FakeLLM
