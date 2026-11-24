using System;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI.Tools
{
    /// <summary>
    /// Something the agent can do. Not tied to MonoBehaviour: implement it directly,
    /// inherit <see cref="AgentTool{TArgs}"/> (one tool per component), or build
    /// <see cref="DelegateTool{TArgs}"/>s in code (many tools per component).
    /// Make tools available by registering them in a <see cref="ToolSet"/>.
    /// </summary>
    public interface IAgentTool
    {
        /// <summary>Unique name the model calls the tool by, e.g. "move_to".</summary>
        string Name { get; }

        /// <summary>Tells the model when to use the tool.</summary>
        string Description { get; }

        /// <summary>JSON Schema of the arguments (see <see cref="ToolSchema"/>), or null for none.</summary>
        JObject Parameters { get; }

        /// <summary>
        /// Runs on the main thread. Return <see cref="ToolResult.Fail"/> for expected failures
        /// (locked door); exceptions are caught by the agent and reported as failed observations.
        /// Honour <paramref name="ct"/>: it fires when the player stops the agent.
        /// </summary>
        Awaitable<ToolResult> Execute(JObject args, CancellationToken ct);
    }

    /// <summary>What the model sees after a tool runs (an observation).</summary>
    public readonly struct ToolResult
    {
        public readonly string Content;
        public readonly bool IsError;

        private ToolResult(string content, bool isError)
        {
            Content = content;
            IsError = isError;
        }

        public static ToolResult Ok(string content) => new(content, false);
        public static ToolResult Fail(string content) => new(content, true);

        /// <summary>Wraps a result that's ready now, for tools that finish instantly.</summary>
        public static Awaitable<ToolResult> Completed(ToolResult result)
        {
            var source = new AwaitableCompletionSource<ToolResult>();
            source.SetResult(result);
            return source.Awaitable;
        }
    }

    /// <summary>Argument type for tools that take no arguments.</summary>
    public sealed class NoArgs
    {
    }

    internal static class ToolArgs
    {
        public static bool TryParse<TArgs>(JObject args, out TArgs value, out string error)
        {
            error = null;
            args ??= new JObject();
            try
            {
                if (typeof(TArgs) == typeof(JObject))
                    value = (TArgs)(object)args;
                else if (typeof(TArgs) == typeof(NoArgs))
                    value = (TArgs)(object)new NoArgs();
                else
                    value = args.ToObject<TArgs>();
                return true;
            }
            catch (Exception e) when (e is JsonException or ArgumentException or InvalidCastException)
            {
                value = default;
                error = $"Invalid arguments: {e.Message}";
                return false;
            }
        }
    }
}
