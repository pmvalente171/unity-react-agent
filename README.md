# AI Transparency (prototype)

A Unity prototype where an LLM agent acts inside the game world and the player can watch it
think. The agent works in a ReAct loop: it writes a short *thought*, takes one *action* (a
tool the game provides, such as walking somewhere or picking up an item), reads the
*observation* the game sends back, and repeats until it gives a *final answer*. Every step
is shown to the player.

## How it fits together

```
Unity game  ──POST /step (history + tools)──▶  agent_server  ──▶  LLM (OpenAI, vLLM, ...)
            ◀──────── one action or final ────
```

- **The Unity game** (`Assets/`) runs the loop and keeps the history. It executes the tools
  and shows each step in the UI.
- **The agent server** (`agent_server/`) is a stateless Python service. Given the history and
  the available tools, it asks the model for the next step and checks the answer. It also
  owns the choice of model and the system prompt; the game can't change either.

The two halves talk over a small HTTP API described in
[agent_server/CONTRACT.md](agent_server/CONTRACT.md).

## Getting started

1. **Start the server.** See [agent_server/README.md](agent_server/README.md). In short:
   copy `agent_server/.env.example` to `agent_server/.env`, fill in the model's key or host,
   and run `docker compose up --build` in `agent_server/`. It listens on
   `http://localhost:5000`.
2. **Open the project in Unity 6** (6000.3, URP).
3. **Point the game at the server** (only if it isn't on `localhost:5000`): create
   **Create → AI → Agent Server Settings** and save it as
   `Assets/Resources/AgentServerSettings.asset`. That file is gitignored, so a private host
   stays local. Don't put API keys in it: anything in `Resources/` ships inside builds.
4. **Open a demo scene** (`Assets/Demo/<DemoName>/<DemoName>.unity`) and press Play.

## Demos

| Demo | What the agent does |
|---|---|
| `CharacterDemo` | Walks a capsule around four rooms (`look_around`, `move_to`, `say`). |
| `KitchenDemo` | Cooks timed orders in a restaurant kitchen, one kitchen action per step. |
| `CoopKitchenDemo` | Two agents share a kitchen and coordinate through a pass counter and a notes board. |
| `CheckersDemo` | Plays checkers against the player and consults a second "planner" agent before moving. |
| `ChessDemo` | The same setup as `CheckersDemo`, for chess. |

## Repository layout

| Path | Contents |
|---|---|
| `Assets/Scripts/AI/` | The agent client: `Agent` (runs the loop), `Connector` (HTTP), the contract types, tools and `ToolSet`s. |
| `Assets/Scripts/GameArchitecture/` | Reusable building blocks (singletons, pooling, ScriptableObject events and variables). |
| `Assets/Scripts/UI/` | The chat box that shows the agent's messages and steps. |
| `Assets/Demo/` | The demos, plus `Shared/` code they have in common. |
| `agent_server/` | The Python ReAct server, its tests and its model configuration. |

## License

[MIT](LICENSE)
