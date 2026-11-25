"""Maps a model card's `provider` to the LLM class that implements it."""

from __future__ import annotations

from llm.base import LLM
from llm.model_card import ModelCard, ModelConfigError
from llm.openai_llm import OpenAILLM

PROVIDERS: dict[str, type[LLM]] = {
    "openai": OpenAILLM,  # also any OpenAI-compatible server, via base_url
}


def build_llm(card: ModelCard) -> LLM:
    try:
        cls = PROVIDERS[card.provider]
    except KeyError:
        raise ModelConfigError(
            f"model {card.name!r}: unknown provider {card.provider!r}, expected one of {sorted(PROVIDERS)}"
        ) from None
    return cls(card)
