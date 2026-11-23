"""Model cards: what a given model deployment can and cannot do.

Cards live in config/models.yaml rather than in code, because capabilities
depend on the deployment and not only on the model (for example, a vLLM server
only supports native tool calls if it was launched with tool calling enabled).
"""

from __future__ import annotations

import os
from dataclasses import dataclass, fields
from pathlib import Path

import yaml


class ModelConfigError(Exception):
    """The model config file is missing, malformed or inconsistent."""


@dataclass(frozen=True)
class ModelCard:
    name: str                       # key in models.yaml
    provider: str                   # which LLM class to use, e.g. "openai"
    model_id: str                   # the name the provider's API expects
    base_url: str | None = None     # set for OpenAI-compatible servers (vLLM, Ollama, ...)
    api_key_env: str | None = None  # name of the env var holding the API key
    context_window: int | None = None
    max_output_tokens: int | None = None
    supports_native_tools: bool = True
    supports_parallel_tools: bool = False
    supports_system_role: bool = True
    emits_reasoning: bool = False   # hidden reasoning that never carries across providers

    @property
    def api_key(self) -> str | None:
        return os.environ.get(self.api_key_env) if self.api_key_env else None


_CARD_FIELDS = {f.name for f in fields(ModelCard)} - {"name"}


def load_cards(path: str | Path) -> tuple[dict[str, ModelCard], str]:
    """Load every card in the file. Returns (cards by name, name of the active card)."""
    path = Path(path)
    try:
        data = yaml.safe_load(path.read_text(encoding="utf-8"))
    except FileNotFoundError as e:
        raise ModelConfigError(f"model config not found: {path}") from e
    except yaml.YAMLError as e:
        raise ModelConfigError(f"model config is not valid YAML: {e}") from e

    if not isinstance(data, dict) or not isinstance(data.get("models"), dict):
        raise ModelConfigError(f"{path}: expected a top-level 'models' mapping")

    cards = {}
    for name, spec in data["models"].items():
        if not isinstance(spec, dict):
            raise ModelConfigError(f"{path}: model {name!r} must be a mapping")
        unknown = set(spec) - _CARD_FIELDS
        if unknown:
            raise ModelConfigError(f"{path}: model {name!r} has unknown fields {sorted(unknown)}")
        missing = {"provider", "model_id"} - set(spec)
        if missing:
            raise ModelConfigError(f"{path}: model {name!r} is missing {sorted(missing)}")
        cards[name] = ModelCard(name=name, **spec)

    active = data.get("active")
    if active not in cards:
        raise ModelConfigError(f"{path}: 'active' must name one of {sorted(cards)}, got {active!r}")
    return cards, active


def load_active_card(path: str | Path) -> ModelCard:
    """The card the server should use. The ACTIVE_MODEL env var overrides the file's 'active'."""
    cards, active = load_cards(path)
    override = os.environ.get("ACTIVE_MODEL")
    if override:
        if override not in cards:
            raise ModelConfigError(f"ACTIVE_MODEL={override!r} is not one of {sorted(cards)}")
        active = override
    return cards[active]
