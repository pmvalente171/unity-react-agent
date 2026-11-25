# agent_server: ReAct backend

Stateless Flask server that gives the Unity game one ReAct step per request.
**Unity owns the loop and the history**: it POSTs the full history plus its tools to
`/step`, and the server returns one action (a tool call) or a final answer.
Project-wide context (the Unity side, the two sessions) is in the root `../CLAUDE.md`.

**`CONTRACT.md` is the source of truth for the wire format.** Any change to requests,
responses or error codes must update `CONTRACT.md` *and* be sent to the Unity-side Claude
session (find it with ListAgents; it runs from the repo root).

## Layout

| Path | Role |
|---|---|
| `server.py` | HTTP only: JSON → `core/types` → `agent.step()` → JSON. Error shape `{"error", "code"}`. |
| `core/types.py` | Provider-neutral dataclasses shared by every layer. |
| `agent/react_agent.py` | Everything agent-related: the system prompt, tool-call checks (name + JSON schema), one retry with a correction, unique action ids. |
| `llm/base.py` | Abstract `LLM.generate(system, messages, tools)`, `LLMError`, `ContextTooLongError`. |
| `llm/openai_llm.py` | OpenAI and any OpenAI-compatible server (vLLM, Ollama) via `base_url`. |
| `llm/model_card.py` + `config/models.yaml` | What each model deployment can do. `active` picks the model; `ACTIVE_MODEL` env var overrides it. |
| `llm/registry.py` | Maps a card's `provider` to its LLM class. |
| `tools/probe_model.py` + `probe_model.sh` | Probe a deployment to fill in its card. |

## Design rules

- **Stay stateless.** No sessions or stored history on the server; keep `agent.step(history)` a pure function of its inputs.
- **Nothing provider-specific outside `llm/`.** The agent and server only see `core/types`.
- **Capability differences go through the model card**, never `if provider == ...` checks in the agent. Cards live in config because capabilities depend on the deployment, not just the model.
- **The model and system prompt are server settings only.** The client can't choose them (`system` in a request is rejected with 400).
- **Keep the LLM hierarchy flat**: one subclass per provider, registered in `llm/registry.py`.

## Common changes

- **New provider:** subclass `LLM` in `llm/<name>_llm.py`, translate to/from `core/types`, turn every provider failure into `LLMError` (context overflow into `ContextTooLongError`), and register it.
- **New model card field:** add it to `ModelCard` *and* to the field comment block at the top of `config/models.yaml`. Unknown fields in the YAML are rejected on purpose.
- **New model:** add a card in `config/models.yaml`. API keys go in env vars named by `api_key_env`, never in the file. Hosts and headers can use `${VAR}` so private hosts and secrets stay in `.env`.
- **Filling in a card's capabilities:** the user runs `./probe_model.sh <card>` (it reads `.env`, which Claude may not read) and pastes the output; it lists the served models and tests system role, native tools and a tool-result round trip.

## Running

`docker compose up --build` starts the server on port 5000 with the env vars from `.env`.
The active model is `gemma` (Gemma 4 on vLLM). The user starts it, since it reads `.env`.

## Testing

Python isn't installed on the dev machine, so tests run in Docker (Docker Desktop must be running):

```sh
./run_tests.sh            # whole suite
./run_tests.sh -k agent   # extra args go to pytest
```

Tests use `FakeLLM` (`tests/conftest.py`) or a fake OpenAI client, never real API calls. Every
behaviour change gets a test.

## Not done yet

- Text-based ReAct fallback for models without native tool calling (currently a 501 `not_supported`).
- `gemma`: native tools verified; system role and parallel flag not yet. `ollama`: unprobed, still needs a `model_id`.
- Changing the model needs a server restart.
