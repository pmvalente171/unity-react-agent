# AI_Transparency.2026.proto

Unity prototype where an LLM agent acts inside the game world through a ReAct loop
(thought → action → observation). The agent's reasoning is meant to be visible to the
player, so keep each step (thought, action, observation, final answer) available to
gameplay and UI.

## Two halves, two Claude sessions

| Part | Where | Owner session |
|---|---|---|
| Unity game (frontend, runs the loop) | repo root, `Assets/` | starts at the repo root |
| ReAct step server (Python, Flask, Docker) | `agent_server/` | starts in `agent_server/` |

- **Unity owns the loop and the history.** The server is stateless: Unity POSTs the full
  history plus the tools it can run to `/step` and gets back one action or a final answer.
  Unity runs the tool, appends the returned step (as an `assistant` message, without `type`)
  and then its observation, and calls `/step` again.
- **The model and the system prompt are server-side settings** (`agent_server/config/models.yaml`).
  Unity never sends either; a `system` field is rejected with 400. Don't add prompt or model
  fields on the Unity side.
- **Connection:** the server listens on port 5000 by default (`PORT` env var; the Docker image
  exposes 5000). `GET /health` returns `{"status":"ok","model":...}`. Keep the backend base URL
  configurable (a ScriptableObject or serialized field), never hard-coded, and use `/health` to
  check the server is up.
- **`agent_server/CONTRACT.md` is the source of truth for the wire format.** Build the C#
  side against it and don't copy its details here. Backend internals are covered in
  `agent_server/CLAUDE.md`.
- **Contract changes:** any change to the `/step` requests, responses or error codes must
  update `CONTRACT.md` *and* be sent to the other session. Find it with ListAgents and
  identify it by its folder (repo root or `agent_server/`), not by name, because session names
  change when a session restarts. Don't change the contract just from one side.

## Unity side

- Unity **6000.3** (Unity 6), URP, Input System, UI Toolkit.
- Scripts live in `Assets/Scripts/`:
  - `AI/`: namespace `AI`. `Connector` (a static class, no scene object) talks to the backend;
    its address comes from an optional `AgentServerSettings` asset in `Resources/`
    (default `http://localhost:5000`). `Assets/Resources/AgentServerSettings.asset` is gitignored
    (local, may hold a private host). Never put API keys or the lab/vLLM host in it: Resources
    ship inside builds. Those stay in `agent_server/.env`.
  - `GameArchitecture/`: namespace `GameArchitecture`. Reusable building blocks: `Singleton<T>`,
    `Optional<T>`, `CoroutineHandle`, `ObjectPool`, ScriptableObject patterns (`GameEvent`,
    `EventListener`, `Scriptable*` variables), `Store`. Check here before writing a new helper,
    and put new generic helpers here.
  - `Gameplay/`: game-specific code.
- Demos live in `Assets/Demo/<DemoName>/` (namespace `Demo.<DemoName>`): `Scripts/`, `Editor/`
  (a scene-builder menu under **Tools → AI**), and `Generated/` assets the builder writes.
  Rebuild demo scenes with their menu rather than hand-editing them. Code used by more than one
  demo goes in `Assets/Demo/Shared/` (namespace `Demo.Shared`): `DemoCharacter` (NavMesh walking,
  speech), `WorldOverlay` (labels that follow objects) and `Editor/DemoBuilder` (scene-builder helpers).
  - `CharacterDemo`: a capsule the agent walks around four rooms (`look_around`, `move_to`, `say`).
  - `KitchenDemo`: a cooking game. Stations (`Station` subclasses) register themselves; `Hands` holds
    one item; `KitchenTools` tools walk to their station by themselves (one agent step = one kitchen
    action). Recipes are data in `Food.cs`. `KitchenDirector` runs the restaurant: it spawns timed
    orders, scores served dishes (the `ServingHatch.Validate` hook refuses dishes nobody ordered), and
    drives the agent: one `agent.Send(prompt, "Kitchen")` per order whenever the agent is idle. The
    agent is driven by game events, not the chat. Planned: pausing game time while the agent is thinking.
  - `CoopKitchenDemo`: multi-agent. Two cooks (`PrepCook`, `LineCook`, both `CoopCook`), each with its own
    `Agent` and `ToolSet`, work in separate zones and share a middle zone: a `PassCounter` for handing food
    over and a `PlanBoard` for notes (`post_note`, `wait_for_partner`). A cook reaches only the stations under
    its zone's and the middle zone's transforms. `CoopDirector` sends each idle cook its role plus the orders.
    It reuses KitchenDemo's food and stations unchanged (namespace `Demo.KitchenDemo`); leave KitchenDemo as is.
- `Agent.Send(text, sender)` raises `MessageSent(text, sender)`; the chat shows every message given to
  the agent (the player's, and game-driven ones like orders) through that event.
- Prefer the existing ScriptableObject event pattern (`GameEvent` / `EventListener`) for
  connecting systems, e.g. letting UI react to agent steps, over direct references.
- `Singleton<T>` is scene-placed (not created on demand). Subclasses overriding
  `Awake`/`OnDestroy` must call `base`.
- No assembly definitions (`.asmdef`). Everything compiles into `Assembly-CSharp`.
- HTTP goes through `UnityWebRequest`. For JSON with dictionaries or free-form tool args, use
  Newtonsoft (`com.unity.nuget.newtonsoft-json`), not `JsonUtility`.
- **Agent pieces** (`Assets/Scripts/AI/`): `Contract/StepContract.cs` mirrors CONTRACT.md;
  `Connector` does HTTP only; `Agent` runs the loop and raises C# events; tools are
  `IAgentTool`s registered in a `ToolSet` ScriptableObject. **Write tools as methods marked
  `[AgentTool, Description("...")]`** and call `toolSet.RegisterFrom(this)` / `UnregisterFrom(this)`
  in OnEnable/OnDisable; name and argument schema come from the method signature (`MethodTool`).
  Use `DelegateTool<TArgs>` only for nested arguments or runtime-built tools. The agent never
  references tools directly, and UI (`Assets/Scripts/UI/ChatBox`) never references the agent:
  `AgentChatBridge` wires them. Keep the `ToolSet` an explicit Inspector reference (one per
  agent, for multi-agent setups); elsewhere prefer defaults/auto-lookup over required wiring.

## Rules

- **Nothing non-Unity under `Assets/`.** Unity imports everything there and makes `.meta`
  files. Python, Docker and similar stay in `agent_server/`.
- **`.meta` files:** commit them together with their asset. Never hand-edit or regenerate the
  GUIDs. When moving/deleting an asset, move/delete its `.meta` with it.
- Don't edit scenes (`*.unity`), prefabs or ProjectSettings YAML by hand unless asked. Describe
  the Editor steps instead.
- Don't touch generated files: `Library/`, `Temp/`, `Logs/`, `obj/`, `*.csproj`, `*.sln`.

## Verifying C# changes

There's no command-line build. Unity compiles when the Editor refreshes. To check for
compile errors after an edit, ask the user to focus the Editor, then read the end of the
Editor log with the Read tool: `~/AppData/Local/Unity/Editor/Editor.log` (look for `error CS`). Don't claim C#
compiles without that check; say it's unverified.
