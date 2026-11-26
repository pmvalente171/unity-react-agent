# Adding an LLM

The game never talks to a model directly. It talks to `agent_server`, and the server picks
the model. So changing or adding an LLM is server-only work: **the Unity project doesn't
change at all.**

There are two cases:

1. **The model speaks the OpenAI Chat Completions API.** That covers OpenAI itself and most
   self-hosted servers (vLLM, Ollama, LM Studio, llama.cpp's server, and so on). Adding it
   takes **a few lines of YAML**.
2. **It has its own API.** You write one small Python class.

Either way, the model needs **native tool calling** (function calling). Models without it are
rejected with `501 not_supported` for now.

## 1. Add a model card (no code)

Each model is described by a *model card* in
[`agent_server/config/models.yaml`](../agent_server/config/models.yaml). Add one under
`models:`:

```yaml
  my-model:
    provider: openai                  # the OpenAI-compatible client
    base_url: ${MY_MODEL_HOST}/v1     # leave out for OpenAI itself
    model_id: qwen3-32b               # the name the API expects
    api_key_env: MY_MODEL_KEY         # name of the env var with the key, not the key
    supports_native_tools: true
    supports_system_role: true
    can_disable_parallel_tools: false
```

Put the secrets in `agent_server/.env`, never in the YAML:

```sh
MY_MODEL_HOST=https://my-server.example
MY_MODEL_KEY=...
ACTIVE_MODEL=my-model   # or set `active: my-model` in models.yaml
```

Restart the server (`docker compose up --build`) and check it with
`curl http://localhost:5000/health`, which names the active model.

### What the capability flags mean

The same model can behave differently depending on how it's served, so these live on the
card rather than in code.

| Flag | Set it to `true` when... | If `false`... |
|---|---|---|
| `supports_native_tools` | the server returns real tool calls (for vLLM, it was started with a tool parser). | the server answers `501` (a text-based fallback isn't written yet). |
| `supports_system_role` | the chat template accepts a `system` message. | the system prompt is put at the start of the first user message instead (always safe). |
| `can_disable_parallel_tools` | the API accepts `parallel_tool_calls: false`. | the flag is never sent; the server just uses the first tool call. |

Not sure? Run the probe. It connects with the card and your `.env`, lists the models the
server has, and tests the system role, tool calls and a full tool round trip:

```sh
cd agent_server
./probe_model.sh my-model
```

The other card fields (`headers`, `context_window`, ...) are documented at the top of
`models.yaml`. `${VAR}` works in `base_url` and in header names and values, so private hosts
stay out of git.

## 2. Add a provider (a little code)

If the API isn't OpenAI-compatible, add a class for it. Everything provider-specific stays
in `agent_server/llm/`; the agent logic never sees it.

1. **Create `llm/<name>_llm.py`** with a subclass of `LLM` (from `llm/base.py`). It has one
   method:

   ```python
   def generate(self, system: str, messages: list[Message], tools: list[ToolSpec]) -> LLMResponse:
   ```

   Translate the provider-neutral types in `core/types.py` into the provider's request,
   and its reply back into an `LLMResponse`: the model's text (the thought, or the final
   answer) and its tool calls. Read settings from `self.card` (model id, key, capability
   flags).
2. **Turn errors into the server's errors.** Raise `LLMError` for any provider failure
   (network, auth, rate limit), and `ContextTooLongError` when the history doesn't fit. The
   game trims its history when it gets the second one.
3. **Register it** in `llm/registry.py`:

   ```python
   PROVIDERS = {
       "openai": OpenAILLM,
       "myprovider": MyProviderLLM,
   }
   ```

4. **Add a card** with `provider: myprovider`, as in case 1.
5. **Test it.** Add tests with a fake client (see `tests/test_openai_llm.py`; tests never
   call a real API) and run `./run_tests.sh`. It needs only Docker.

`llm/openai_llm.py` is the reference implementation to copy from.

## What you don't need to touch

- **The ReAct logic** (`agent/react_agent.py`): the prompt, checking tool calls against their
  schemas, retrying once with a correction. It works the same for every model.
- **The HTTP API** ([CONTRACT.md](../agent_server/CONTRACT.md)) and **the Unity project**.
