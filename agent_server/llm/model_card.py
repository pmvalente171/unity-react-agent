"""Model cards: what a given model deployment can and cannot do.

Cards live in config/models.yaml rather than in code, because capabilities
depend on the deployment and not only on the model (for example, a vLLM server
only supports native tool calls if it was launched with tool calling enabled).
"""

from __future__ import annotations

import os
import re
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
    base_url: str | None = None     # set for OpenAI-compatible servers (vLLM, Ollama, ...); ${VAR} allowed
    api_key_env: str | None = None  # name of the env var holding the API key
    headers: dict[str, str] | None = None  # extra HTTP headers; ${VAR} allowed in names and values
    context_window: int | None = None     # informational for now
    max_output_tokens: int | None = None  # informational for now
    supports_native_tools: bool = True
    can_disable_parallel_tools: bool = False  # API accepts parallel_tool_calls=false
    supports_system_role: bool = True
    emits_reasoning: bool = False   # informational for now: the model returns hidden reasoning

    @property
    def api_key(self) -> str | None:
        return os.environ.get(self.api_key_env) if self.api_key_env else None

    @property
    def resolved_base_url(self) -> str | None:
        """`base_url` with ${VAR} references filled in from the environment."""
        return self._expand(self.base_url, "base_url") if self.base_url else None

    @property
    def resolved_headers(self) -> dict[str, str]:
        """`headers` with ${VAR} references filled in, in names as well as values."""
        return {
            self._expand(k, "headers"): self._expand(v, "headers")
            for k, v in (self.headers or {}).items()
        }

    def _expand(self, text: str, field_name: str) -> str:
        # Resolved when the model is used, not when the file loads, so a card
        # whose env vars are missing only fails if it is the active one.
        def lookup(m: re.Match) -> str:
            value = os.environ.get(m.group(1))
            if not value:
                raise ModelConfigError(
                    f"model {self.name!r}: env var {m.group(1)} (used in {field_name}) is not set"
                )
            return value
        return _ENV_REF.sub(lookup, text)


_ENV_REF = re.compile(r"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")


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
        headers = spec.get("headers")
        if headers is not None and not (
            isinstance(headers, dict) and all(isinstance(v, str) for v in [*headers, *headers.values()])
        ):
            raise ModelConfigError(f"{path}: model {name!r}: 'headers' must map strings to strings")
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
