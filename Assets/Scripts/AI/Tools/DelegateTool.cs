using System;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI.Tools
{
    /// <summary>
    /// A tool built by hand from a delegate. Prefer <see cref="AgentToolAttribute"/>; use this
    /// for nested argument objects (<typeparamref name="TArgs"/> can be any class) or for
    /// names/descriptions/schemas decided at runtime:
    /// <code>
    /// _tools = new IAgentTool[] {
    ///     new DelegateTool&lt;MoveArgs&gt;("move_to", "Walk to a place", MoveSchema, MoveTo),
    ///     new DelegateTool&lt;NoArgs&gt;("pick_up", "Pick up the nearest item", null, PickUp),
    /// };
    /// void OnEnable()  =&gt; toolSet.RegisterAll(_tools);
    /// void OnDisable() =&gt; toolSet.UnregisterAll(_tools);
    /// </code>
    /// <typeparamref name="TArgs"/> is filled from the JSON arguments with Newtonsoft
    /// (use [JsonProperty("snake_name")] on fields), or use <see cref="NoArgs"/> / JObject.
    /// </summary>
    public sealed class DelegateTool<TArgs> : IAgentTool
    {
        private readonly Func<TArgs, CancellationToken, Awaitable<ToolResult>> _run;

        public string Name { get; }
        public string Description { get; }
        public JObject Parameters { get; }

        /// <summary>For tools that take time (await game events, timers, animations).</summary>
        public DelegateTool(string name, string description, JObject parameters,
            Func<TArgs, CancellationToken, Awaitable<ToolResult>> run)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Tool name is required.", nameof(name));
            Name = name;
            Description = description;
            Parameters = parameters;
            _run = run ?? throw new ArgumentNullException(nameof(run));
        }

        /// <summary>For tools that finish instantly.</summary>
        public DelegateTool(string name, string description, JObject parameters, Func<TArgs, ToolResult> run)
            : this(name, description, parameters, WrapSync(run))
        {
        }

        public Awaitable<ToolResult> Execute(JObject args, CancellationToken ct) =>
            ToolArgs.TryParse<TArgs>(args, out var parsed, out var error)
                ? _run(parsed, ct)
                : ToolResult.Completed(ToolResult.Fail(error));

        private static Func<TArgs, CancellationToken, Awaitable<ToolResult>> WrapSync(Func<TArgs, ToolResult> run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            return (args, _) => ToolResult.Completed(run(args));
        }
    }
}
