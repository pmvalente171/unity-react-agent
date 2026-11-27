# ReAct for Unity - A Unity-esque Solution.

The overall goal for this project is to build a strong foundation for build lightweight 
research applications that include Unity and LLMs. We aim to support native tool calling
across all Monobehavior(s) and a quick and intuitive setup for building multi-agent setups.

## The idea: Unity runs the agent

A ReAct agent works in a loop: it writes a short *thought*, takes one *action* (calls a
tool), reads the *observation* that comes back, and repeats until it gives a *final answer*.

Most agent frameworks run that loop on a server, next to the model. Here **the game runs
it**:

```
            ╭─POST /step (history + tools)──▶
Unity game ─╯                                 ╭─agent_server  ──▶  LLM (OpenAI, vLLM, ...)
           ◀────── one action or final ───────╯
```

- **Tools are ordinary C# methods** on your MonoBehaviours. They run on the main thread, can
  take several seconds (walk somewhere, wait for an animation), and fail the way game code
  fails ("the door is locked").
- **Unity keeps the history**, so every thought, action and observation is right there for
  gameplay and UI. Showing the agent's reasoning to the player is just listening to C# events.
- **The server is stateless and tiny.** It asks the model for *one* next step and checks it
  (a known tool, valid arguments). It also owns the model and the base prompt, so swapping
  LLMs never touches the game.
- **Agents come and go with the scene.** A tool is available while its component is enabled.
  Several agents can share a scene, and an agent can be a tool of another agent.

## A small example

Let an agent switch a lamp. You write one component:

```csharp
public class LampTools : MonoBehaviour
{
    [SerializeField] ToolSet toolSet;
    [SerializeField] Light lamp;

    void OnEnable()  => toolSet.RegisterFrom(this);
    void OnDisable() => toolSet.UnregisterFrom(this);

    [AgentTool, Description("Turn the lamp on or off.")]
    string SetLamp(bool on)
    {
        lamp.enabled = on;
        return on ? "The lamp is on." : "The lamp is off.";
    }
}
```

and wire it up in the Editor:

1. **Create → AI → Tool Set** to make a `ToolSet` asset. It's the list of what the agent can do.
2. Add an **`Agent`** component to any GameObject and give it that `ToolSet`.
3. Put **`LampTools`** on the lamp and give it the same `ToolSet` and the `Light`.
4. Copy the **`Chat`** object from a demo scene, so you can talk to the agent and see its steps.

Press Play and type *"It's dark in here."* The chat shows each step:

> **Thought:** The player says it's dark, so I'll turn the lamp on.
> **Action:** `set_lamp {"on": true}`
> **Observation:** The lamp is on.
> **Agent:** Done, the lamp is on.

The method became the tool `set_lamp`: its name, description and argument schema come from
the C# signature. No prompt writing, JSON or HTTP on your side.

## Going further

| Guide | What it covers |
|---|---|
| [Building agents in Unity](docs/unity-agents.md) | Tools in depth, showing steps in your own UI, driving the agent from game events, roles, several agents, sub-agents. |
| [Adding an LLM](docs/adding-models.md) | Pointing the server at another model, or adding a new provider. |
| [agent_server/README.md](agent_server/README.md) | Running, testing and configuring the server. |
| [agent_server/CONTRACT.md](agent_server/CONTRACT.md) | The HTTP API between the game and the server. |

## Getting started

1. **Start the server.** Copy `agent_server/.env.example` to `agent_server/.env`, fill in the
   model's key or host, and run `docker compose up --build` in `agent_server/`. It listens
   on `http://localhost:5000`. See [agent_server/README.md](agent_server/README.md).
2. **Open the project in Unity 6** (6000.3, URP).
3. **Point the game at the server** (only if it isn't on `localhost:5000`): create
   **Create → AI → Agent Server Settings** and save it as
   `Assets/Resources/AgentServerSettings.asset`. That file is gitignored, so a private host
   stays local. Don't put API keys in it: anything in `Resources/` ships inside builds.
4. **Open a demo scene** (`Assets/Demo/<DemoName>/<DemoName>.unity`) and press Play.

## Demos

| Demo | What the agent does |
|---|---|
| `CharacterDemo` | Walks a capsule around four rooms (`look_around`, `move_to`, `say`). The best place to start. |
| `KitchenDemo` | Cooks timed orders in a restaurant kitchen, one kitchen action per step. Orders come from the game, not the chat. |
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
| `docs/` | The guides above. |

## License

[MIT](LICENSE)
