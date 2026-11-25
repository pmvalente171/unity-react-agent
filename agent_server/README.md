# agent_server

The ReAct backend for the Unity game. It's a small, stateless Flask server: Unity sends
the whole conversation so far plus the tools it can run, and the server answers with
one next step: either a tool call (an *action*) or a *final* answer. Unity runs the tool,
adds the result to the history, and asks again.

The wire format is specified in [CONTRACT.md](CONTRACT.md).

## Running

You need Docker. Copy `.env.example` to `.env` and fill in the keys or hosts for the model
you want, then:

```sh
docker compose up --build
```

The server listens on port 5000. Check it with:

```sh
curl http://localhost:5000/health
# {"status": "ok", "model": "gemma", "provider": "openai"}
```

### Choosing the model

Models are described by *model cards* in [config/models.yaml](config/models.yaml). The card
named by `active` is used; setting `ACTIVE_MODEL` in `.env` overrides it. The server reads
the card at startup, so restart it after changing either.

A card says what the deployment can do (native tool calls, a system role, and so on).
Capabilities depend on how a model is served, not only on the model, so they live in config
rather than code. To find out what a deployment supports, run:

```sh
./probe_model.sh <card name>
```

It connects with the same card and `.env` as the server and reports what worked.

### Environment variables

| Variable | Default | Purpose |
|---|---|---|
| `PORT` | `5000` | Port to listen on. |
| `ACTIVE_MODEL` | the file's `active` | Which model card to use. |
| `MODEL_CONFIG` | `config/models.yaml` | Path to the model cards. |
| `LOG_LEVEL` | `INFO` | `INFO` logs one line per step; `DEBUG` also logs each step's thought and action. |
| `HOST` | `127.0.0.1` | Only for `python server.py`; Docker always listens on all interfaces. |

API keys and private hosts are also set in `.env`, under the names each card gives.

## How it's organised

| Path | Role |
|---|---|
| `server.py` | HTTP only: checks the request, turns JSON into the types in `core/`, calls the agent, returns JSON. |
| `core/types.py` | The data types every layer shares. Nothing provider-specific. |
| `agent/react_agent.py` | One ReAct step: the system prompt, checking the model's tool call (known tool, arguments match the schema), one retry with a correction, unique action ids. |
| `llm/` | One class per provider behind the abstract `LLM`. `openai_llm.py` covers OpenAI and any OpenAI-compatible server (vLLM, Ollama). `registry.py` maps a card's `provider` to its class. |
| `config/models.yaml` | The model cards. |
| `tools/probe_model.py` | What `probe_model.sh` runs. |
| `tests/` | Pytest suite. Uses a fake LLM; it never calls a real API. |

Two rules keep the design simple:

- **The server is stateless.** It stores no sessions or history, so `agent.step(history, tools)`
  depends only on its inputs.
- **Provider details stay in `llm/`.** Differences between models go through the model card,
  never `if provider == ...` checks elsewhere.

## Development

Python isn't needed locally: the tests and the linter (ruff) run in a throwaway container.

```sh
./run_tests.sh            # ruff, then the whole test suite
./run_tests.sh -k agent   # extra arguments go to pytest
```

CI runs the same checks on every push that touches `agent_server/`.

### Adding a provider

Subclass `LLM` in `llm/<name>_llm.py`. Translate to and from `core/types`, turn every provider
failure into `LLMError` (and context overflow into `ContextTooLongError`), and register the
class in `llm/registry.py`.

### Updating dependencies

`requirements.in` lists the direct dependencies with the version ranges the code supports.
`requirements.txt` pins them and everything they pull in, so builds are repeatable. After
changing `requirements.in`, regenerate the pins:

```sh
MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd -W 2>/dev/null || pwd):/src:ro" python:3.12-slim sh -c \
  "pip install -q -r /src/requirements.in && pip freeze --exclude pip --exclude setuptools --exclude wheel"
```

and paste the output into `requirements.txt` under its header comment. Run `./run_tests.sh` afterwards.

## Not done yet

- Models without native tool calling (a text-based ReAct fallback). Until then such a model
  returns `501 not_supported`.
- The `ollama` card needs a real `model_id`; `./probe_model.sh ollama` lists what the server has.
- `context_window`, `max_output_tokens` and `emits_reasoning` are recorded on the cards but
  not used yet.
