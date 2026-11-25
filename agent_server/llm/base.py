"""Abstract LLM interface. Everything provider-specific lives in subclasses."""

from __future__ import annotations

from abc import ABC, abstractmethod

from core.types import LLMResponse, Message, ToolSpec
from llm.model_card import ModelCard


class LLMError(Exception):
    """Any failure talking to the provider (network, auth, rate limit, bad response)."""


class ContextTooLongError(LLMError):
    """The request does not fit in the model's context window. The client should trim history."""


class LLM(ABC):
    def __init__(self, card: ModelCard):
        self.card = card

    @abstractmethod
    def generate(
        self, system: str, messages: list[Message], tools: list[ToolSpec]
    ) -> LLMResponse:
        """Ask the model for its next reply.

        Implementations translate to/from the provider format, respect the
        model card (e.g. no system role), and raise LLMError on any failure.
        """
