"""Check what a model deployment supports, to fill in its model card.

Usage: python tools/probe_model.py <card name>     (e.g. gemma, ollama)
Or, with no local Python:  ./probe_model.sh <card name>

It connects exactly as the server would (same card, same env vars), lists the
models the server offers, then tries: a plain reply, a system message, native
tool calls, and a full round trip with a tool result. It prints results only,
never keys, headers or URLs.
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from llm.model_card import load_cards  # noqa: E402
from llm.registry import build_llm  # noqa: E402

TOOLS = [{"type": "function", "function": {
    "name": "move_to",
    "description": "Walk to a place in the level.",
    "parameters": {"type": "object", "properties": {"place": {"type": "string"}}, "required": ["place"]},
}}]
ASK = [
    {"role": "system",
     "content": "You are an agent in a game. Explain your reasoning briefly, then call exactly one tool."},
    {"role": "user", "content": "Go to the kitchen."},
]


def main(card_name: str) -> None:
    cards, _ = load_cards(Path(__file__).resolve().parents[1] / "config" / "models.yaml")
    if card_name not in cards:
        sys.exit(f"unknown card {card_name!r}; cards: {sorted(cards)}")
    card = cards[card_name]
    client = build_llm(card).client
    model = card.model_id

    try:
        print("models offered:", [m.id for m in client.models.list().data])
    except Exception as e:
        print("models list FAILED:", _err(e))
    if model in ("", "SET-ME"):
        print(f"\nSet model_id for {card_name!r} in config/models.yaml to one of the models above, then rerun.")
        return

    def attempt(label, **kw):
        try:
            r = client.chat.completions.create(model=model, max_tokens=300, timeout=120, **kw)
        except Exception as e:
            print(f"[{label}] FAILED: {_err(e)}")
            return None
        m = r.choices[0].message
        calls = [(c.id, c.function.name, c.function.arguments) for c in (m.tool_calls or [])]
        extra = [k for k, v in (m.model_extra or {}).items() if v]
        print(f"[{label}] ok: content={m.content!r:.200} tool_calls={calls} extra_fields={extra}")
        return r

    print()
    attempt("plain reply", messages=[{"role": "user", "content": "Say hi in three words."}])
    attempt("system message", messages=[
        {"role": "system", "content": "Always answer in French."},
        {"role": "user", "content": "Say hi in three words."},
    ])
    r = attempt("native tools", messages=ASK, tools=TOOLS)
    attempt("native tools + parallel_tool_calls flag", messages=ASK, tools=TOOLS, parallel_tool_calls=False)
    if r and r.choices[0].message.tool_calls:
        c = r.choices[0].message.tool_calls[0]
        attempt("tool result round trip", tools=TOOLS, messages=ASK + [
            {"role": "assistant", "content": r.choices[0].message.content or "", "tool_calls": [
                {"id": c.id, "type": "function",
                 "function": {"name": c.function.name, "arguments": c.function.arguments}}]},
            {"role": "tool", "tool_call_id": c.id, "content": "Arrived at kitchen."},
        ])

    print("\nHow to read this for the card:")
    print("  system message: replied in French -> supports_system_role: true")
    print("  native tools: tool_calls not empty -> supports_native_tools: true")
    print("  parallel flag: ok -> can_disable_parallel_tools may be true; FAILED -> false")
    print("  extra_fields such as reasoning_content -> emits_reasoning: true")


def _err(e: Exception) -> str:
    return f"{type(e).__name__}: {str(e)[:300]}"


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
