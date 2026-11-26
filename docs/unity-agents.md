# Building agents in Unity

This guide picks up where the lamp example in the [README](../README.md) stops. All the code
is in `Assets/Scripts/AI/`, and each section points to a demo that uses the idea.

## The pieces

| Piece | What it is | You... |
|---|---|---|
| `Agent` | A component that runs the ReAct loop and keeps the history. | add one per agent. |
| `ToolSet` | A ScriptableObject listing the tools an agent can use right now. | create one per agent (**Create → AI → Tool Set**). |
| `[AgentTool]` method | A tool. | write these on your own components. |
| `ChatBox` + `AgentChatBridge` | A ready-made chat panel and the glue between it and an agent. | copy the `Chat` object from a demo scene, or skip them and use your own UI. |
| `Connector` | The HTTP client for the server. | never touch it. Its address comes from an optional `AgentServerSettings` asset. |

Nothing references anything else directly. Tools and the agent only share a `ToolSet`
asset, and the chat only listens to the agent's events. So a tool can live on any object,
appear and disappear with it, and be reused by another agent that uses the same `ToolSet`.

## Writing tools

A tool is a method marked `[AgentTool, Description("...")]` on a component that registers
itself with a `ToolSet` while it's enabled:

```csharp
void OnEnable()  => toolSet.RegisterFrom(this);
void OnDisable() => toolSet.UnregisterFrom(this);
```

The model sees whatever is registered at each step, so tools come and go with the scene.

**What the model sees comes from the signature.** `MoveTo` becomes `move_to`, the
`Description` attributes on the method and on its parameters become the documentation, and
the parameter types become a JSON Schema. Enums turn into a fixed list of choices, and a
parameter with a default value is optional.

**Supported types:** parameters can be strings, bools, numbers, enums, arrays or lists of
those, or a raw `JObject`. A method can return `string`, `ToolResult` or nothing, or an
`Awaitable` of those.

**Long actions** are `async`. Add a `CancellationToken` parameter (the model never sees it)
so the player can stop the agent mid-walk:

```csharp
[AgentTool, Description("Walk to a named place.")]
async Awaitable<ToolResult> MoveTo([Description("e.g. \"kitchen\"")] string place, CancellationToken ct)
```

**Failures are normal answers.** Return `ToolResult.Fail("The door is locked.")` and the
model is told the action failed, so it can try something else. An exception thrown by a tool
is caught and reported the same way.

**Write observations for the model.** What a tool returns is all the model knows about the
world. A `look_around` tool that lists what's nearby is usually the first tool an agent needs.

Example: `Assets/Demo/CharacterDemo/Scripts/CharacterTools.cs`.

For nested arguments or tools built at runtime, use `DelegateTool<TArgs>` instead of a method.

## Showing the agent's steps

`Agent` raises a C# event for every part of a step:

| Event | When |
|---|---|
| `MessageSent(text, sender)` | A task was given to the agent (by the player or by the game). |
| `ThoughtReceived(thought)` | The model explained what it's about to do. |
| `ActionStarted(action)` | A tool is about to run. |
| `ObservationReceived(action, result)` | The tool finished. |
| `FinalAnswer(answer)` | The task is done. |
| `Failed(error)` / `Cancelled()` | The run ended early. |
| `StateChanged(state)` | `Idle`, `Thinking` (waiting for the model) or `RunningTool`. |

`AgentChatBridge` turns these into chat lines. For anything else (a thought bubble over a
character's head, a "thinking..." animation), subscribe to the same events. The full history
is also available as `agent.History`.

## Driving the agent from the game

The chat is optional. Any script can give the agent a task:

```csharp
if (!agent.IsBusy)
    agent.Send("A new order came in: tomato soup.", "Kitchen");
```

The second argument only labels the message in the chat. `Send` runs until the agent gives a
final answer, so the usual pattern is to wait until `IsBusy` is false and then send the next
task. Use `agent.Cancel()` to stop it, and `agent.ResetConversation()` to start over with an
empty history.

Example: `KitchenDirector` in `Assets/Demo/KitchenDemo/` sends each new order to the agent.

## Giving an agent a role

The base system prompt lives on the server (so the ReAct format rules always hold), but each
`Agent` has an **Instructions** field that's added after it: a role, rules, a tone of voice.
You can also set it from code with `agent.Instructions`.

## Several agents

Give each agent its own `Agent` component and its own `ToolSet`. Tools decide which agent
gets them by the `ToolSet` they register with. For shared things, register the same tool with
both sets.

Example: `CoopKitchenDemo` has two cooks with their own zones, a shared counter for passing
food, and a notes board (`post_note`, `wait_for_partner`) for coordinating.

## Sub-agents

An agent can be a tool of another agent. `SubAgentTool` wraps an `Agent`: the parent calls
it with a task, the sub-agent runs its own loop with its own `ToolSet`, and its final answer
comes back as the observation.

```csharp
_planner = new SubAgentTool("consult_planner", "Ask the planner to look ahead.", plannerAgent, plannerInstructions);
void OnEnable()  => toolSet.Register(_planner);
void OnDisable() => toolSet.Unregister(_planner);
```

If the sub-agent has a chat bridge too, turn its `listenToPlayer` off, so only the parent
gives it tasks.

Example: in `CheckersDemo` and `ChessDemo`, the AI player consults a planner whose only tool,
`simulate`, plays moves on a copy of the board.

## Good to know

- **One action per step.** Each step costs one model call, so make one tool call do one
  meaningful thing. In `KitchenDemo`, "chop the tomato" walks to the board by itself.
- **`maxSteps`** on `Agent` (default 10) stops a run that never finishes.
- **Long conversations are trimmed automatically.** When the history no longer fits the
  model, the agent drops the oldest steps and keeps the first task.
- Tools always run on the main thread, so they can use any Unity API.
