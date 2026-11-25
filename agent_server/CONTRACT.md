# Agent server ↔ Unity contract

The server holds no state. **Unity owns the ReAct loop and the history.** On every
step Unity sends the full history plus the tools it can execute, and the server
returns exactly one next step: an action (one tool call) or a final answer.

The model is chosen on the server (`config/models.yaml`). The client cannot pick
the model or change the system prompt.

## Endpoints

| Method | Path      | Purpose                         |
|--------|-----------|---------------------------------|
| GET    | `/health` | Liveness + which model is active |
| POST   | `/step`   | Get the next ReAct step          |

`GET /health` → `200 {"status": "ok", "model": "gpt-4.1-mini", "provider": "openai"}`

## POST /step request

```json
{
  "tools": [ToolSpec, ...],
  "messages": [Message, ...]
}
```

Any other top-level field is ignored, **except `system`, which is rejected with 400.**

### ToolSpec

| Field         | Type   | Required | Notes |
|---------------|--------|----------|-------|
| `name`        | string | yes      | Non-empty and unique within `tools` (a duplicate is a 400). The model calls the tool by this name. |
| `description` | string | no       | Tell the model when to use the tool. |
| `parameters`  | object | no       | JSON Schema for the arguments. Defaults to `{"type":"object","properties":{}}` when missing or `null`. Must be a valid JSON Schema (otherwise 400). The server checks the model's arguments against it before returning an action. |

```json
{
  "name": "move_to",
  "description": "Walk to a named place in the level.",
  "parameters": {
    "type": "object",
    "properties": { "place": { "type": "string", "enum": ["kitchen", "hall"] } },
    "required": ["place"]
  }
}
```

### Message

`messages` must be non-empty and in chronological order. There are three roles:

| `role`        | Fields | Meaning |
|---------------|--------|---------|
| `user`        | `content` (string) | What the player or game asked for. |
| `assistant`   | `thought` (string), and **exactly one of** `action` `{id, tool, args}` or `answer` (string). Both or neither is a 400; a `null` field counts as missing. | A previous step the server returned. Send it back exactly as you received it (drop `type`). |
| `observation` | `action_id` (string, required), `content` (string), `is_error` (bool, optional, default `false`) | The result of running an action. `action_id` must equal the `action.id` it answers. |

Tool results go back as an **`observation`** message, not a `tool` role.
After every `assistant` message with an `action`, send exactly one `observation`
with the same id before asking for the next step.

**Failed tool runs:** if a tool fails on the Unity side (an unreachable place, a locked door),
still send an `observation`, with `"is_error": true` and a short description of what went
wrong in `content`, e.g. `{"role":"observation","action_id":"call_1","content":"kitchen is locked","is_error":true}`.
The server makes sure the model is told explicitly that the action failed.

## POST /step response (200)

Action: Unity runs the tool, appends this step and an observation, then calls `/step` again.

```json
{
  "type": "action",
  "thought": "The kitchen is north, I'll walk there.",
  "action": { "id": "call_abc123", "tool": "move_to", "args": { "place": "kitchen" } }
}
```

Final: the task is finished, so stop the loop and show `answer`.

```json
{ "type": "final", "thought": "", "answer": "I'm in the kitchen now." }
```

- `thought` is the model's reasoning, meant to be shown to the player. It can be an empty string.
- `action.id` is always a non-empty string, unique within the conversation (it never repeats an
  id already in `messages`). It usually comes from the model provider; when the provider gives no id
  or reuses one, the server generates `act_<32 hex chars>`. Treat it as an opaque string.
- `action.tool` is always one of the tools you sent, and `args` always matches that tool's
  `parameters` schema. The server retries the model once with a correction if it's wrong.

## Errors

Every error body has the same shape:

```json
{ "error": "human-readable message", "code": "bad_request" }
```

| Status | `code`          | Meaning | What Unity should do |
|--------|-----------------|---------|----------------------|
| 400    | `bad_request`   | Malformed request (bad JSON, missing fields, unknown role, `system` sent, duplicate tool name, invalid `parameters` schema, assistant message with both or neither of `action`/`answer`). | Bug in the client; don't retry. |
| 413    | `context_too_long` | `messages` no longer fits the model's context window. | Trim the oldest turns and retry (see below). |
| 502    | `llm_error`     | The model provider failed (network, auth, rate limit, timeout). | Retry later or show "agent unavailable". |
| 502    | `agent_error`   | The model kept giving an invalid step (unknown tool, bad arguments, empty reply). | Retry the same step, or end the task. |
| 501    | `not_supported` | The active model can't do what's needed (e.g. no tool calling yet). | Server config problem; don't retry. |

### Trimming history after `context_too_long`

Keep the first `user` message (the task), drop the oldest turns after it, and retry.
Always drop an `assistant` action together with its `observation`: an observation whose
action is missing (or the reverse) makes the provider reject the request.

Allow generous timeouts: one step can take several seconds, and the server waits up to 120 s.

## Full example loop

Step 1 request:
```json
{
  "tools": [{ "name": "move_to", "parameters": { "type": "object", "properties": { "place": { "type": "string" } }, "required": ["place"] } }],
  "messages": [{ "role": "user", "content": "Go to the kitchen" }]
}
```
Step 1 response:
```json
{ "type": "action", "thought": "I'll walk to the kitchen.", "action": { "id": "call_1", "tool": "move_to", "args": { "place": "kitchen" } } }
```
Step 2 request (same tools; history grows):
```json
{
  "tools": [ ... ],
  "messages": [
    { "role": "user", "content": "Go to the kitchen" },
    { "role": "assistant", "thought": "I'll walk to the kitchen.", "action": { "id": "call_1", "tool": "move_to", "args": { "place": "kitchen" } } },
    { "role": "observation", "action_id": "call_1", "content": "Arrived at kitchen" }
  ]
}
```
Step 2 response:
```json
{ "type": "final", "thought": "", "answer": "I'm in the kitchen." }
```
Unity can also put this final step in the history (`{"role":"assistant","thought":"","answer":"..."}`)
and add a new `user` message to keep the conversation going.
