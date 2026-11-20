"""Stateless ReAct step server.

Unity owns the loop and the history. On every step it POSTs the full history
plus the tools it can execute; the server asks the LLM for exactly one next
step and returns either an action (tool call) or a final answer.

Wire format (provider-agnostic, so Unity never sees OpenAI-specific shapes):

POST /step
{
  "system": "optional system prompt override",
  "tools": [
    {"name": "move_to", "description": "...", "parameters": {<JSON Schema>}}
  ],
  "messages": [
    {"role": "user", "content": "Go to the kitchen"},
    {"role": "assistant", "thought": "...", "action": {"id": "call_1", "tool": "move_to", "args": {...}}},
    {"role": "observation", "action_id": "call_1", "content": "Arrived at kitchen"},
    {"role": "assistant", "thought": "...", "answer": "..."}
  ]
}

Response:
  {"type": "action", "thought": "...", "action": {"id": "...", "tool": "...", "args": {...}}}
  {"type": "final",  "thought": "...", "answer": "..."}
"""

import json
import os

from flask import Flask, jsonify, request
from openai import OpenAI

MODEL = os.environ.get("OPENAI_MODEL", "gpt-4.1-mini")

DEFAULT_SYSTEM_PROMPT = (
    "You are an agent acting inside a game world. Work step by step. "
    "On every turn, first write a short explanation of your reasoning in plain text "
    "(this is shown to the player), then either call exactly one tool, or, if the task "
    "is done, give your final answer as plain text without calling a tool."
)

app = Flask(__name__)
client = OpenAI()  # reads OPENAI_API_KEY from the environment


class BadRequest(Exception):
    pass


def to_openai_tools(tools):
    out = []
    for t in tools:
        if "name" not in t:
            raise BadRequest("every tool needs a 'name'")
        out.append({
            "type": "function",
            "function": {
                "name": t["name"],
                "description": t.get("description", ""),
                "parameters": t.get("parameters") or {"type": "object", "properties": {}},
            },
        })
    return out


def to_openai_messages(system, messages):
    out = [{"role": "system", "content": system}]
    for i, m in enumerate(messages):
        role = m.get("role")
        if role == "user":
            out.append({"role": "user", "content": m.get("content", "")})
        elif role == "assistant":
            msg = {"role": "assistant", "content": m.get("thought") or m.get("answer") or ""}
            action = m.get("action")
            if action:
                msg["tool_calls"] = [{
                    "id": action["id"],
                    "type": "function",
                    "function": {
                        "name": action["tool"],
                        "arguments": json.dumps(action.get("args") or {}),
                    },
                }]
            elif m.get("thought") and m.get("answer"):
                msg["content"] = f"{m['thought']}\n\n{m['answer']}"
            out.append(msg)
        elif role == "observation":
            if "action_id" not in m:
                raise BadRequest(f"messages[{i}]: observation needs 'action_id'")
            out.append({
                "role": "tool",
                "tool_call_id": m["action_id"],
                "content": str(m.get("content", "")),
            })
        else:
            raise BadRequest(f"messages[{i}]: unknown role {role!r}")
    return out


@app.get("/health")
def health():
    return jsonify({"status": "ok", "model": MODEL})


@app.post("/step")
def step():
    body = request.get_json(silent=True)
    if not isinstance(body, dict):
        return jsonify({"error": "expected a JSON object"}), 400

    messages = body.get("messages")
    if not isinstance(messages, list) or not messages:
        return jsonify({"error": "'messages' must be a non-empty list"}), 400

    try:
        oa_messages = to_openai_messages(body.get("system") or DEFAULT_SYSTEM_PROMPT, messages)
        oa_tools = to_openai_tools(body.get("tools") or [])
    except (BadRequest, KeyError, TypeError) as e:
        return jsonify({"error": f"bad request: {e}"}), 400

    kwargs = {"model": MODEL, "messages": oa_messages}
    if oa_tools:
        kwargs["tools"] = oa_tools
        kwargs["parallel_tool_calls"] = False  # one action per ReAct step

    try:
        completion = client.chat.completions.create(**kwargs)
    except Exception as e:  # surface provider errors to Unity instead of a bare 500
        return jsonify({"error": f"LLM call failed: {e}"}), 502

    reply = completion.choices[0].message
    thought = reply.content or ""

    if reply.tool_calls:
        call = reply.tool_calls[0]
        try:
            args = json.loads(call.function.arguments or "{}")
        except json.JSONDecodeError:
            args = {"_raw": call.function.arguments}
        return jsonify({
            "type": "action",
            "thought": thought,
            "action": {"id": call.id, "tool": call.function.name, "args": args},
        })

    return jsonify({"type": "final", "thought": "", "answer": thought})


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=int(os.environ.get("PORT", 5000)), debug=True)
