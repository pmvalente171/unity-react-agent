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
  - `AI/`: namespace `AI`. `Connector` (a `Singleton<Connector>`) talks to the backend.
  - `GameArchitecture/`: namespace `GameArchitecture`. Reusable building blocks: `Singleton<T>`,
    `Optional<T>`, `CoroutineHandle`, `ObjectPool`, ScriptableObject patterns (`GameEvent`,
    `EventListener`, `Scriptable*` variables), `Store`. Check here before writing a new helper,
    and put new generic helpers here.
  - `Gameplay/`: game-specific code.
- Prefer the existing ScriptableObject event pattern (`GameEvent` / `EventListener`) for
  connecting systems, e.g. letting UI react to agent steps, over direct references.
- `Singleton<T>` is scene-placed (not created on demand). Subclasses overriding
  `Awake`/`OnDestroy` must call `base`.
- No assembly definitions (`.asmdef`). Everything compiles into `Assembly-CSharp`.
- HTTP goes through `UnityWebRequest`. For JSON with dictionaries or free-form tool args, use
  Newtonsoft (`com.unity.nuget.newtonsoft-json`), not `JsonUtility`.
- **Agent pieces** (`Assets/Scripts/AI/`): `Contract/StepContract.cs` mirrors CONTRACT.md;
  `Connector` does HTTP only; `Agent` runs the loop and raises C# events; tools implement
  `IAgentTool` and register in a `ToolSet` ScriptableObject, either as one component per
  tool (`AgentTool<TArgs>`) or several built in code (`DelegateTool<TArgs>`). The agent never
  references tools directly, and UI (`Assets/Scripts/UI/ChatBox`) never references the agent:
  `AgentChatBridge` wires them.

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
