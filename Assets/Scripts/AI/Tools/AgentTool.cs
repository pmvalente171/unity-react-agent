using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI.Tools
{
    /// <summary>
    /// Base for a component that is exactly one tool. It registers itself in its ToolSet
    /// while enabled, so enabling/disabling the component shows/hides the tool to the agent.
    /// For several tools on one component, use <see cref="DelegateTool{TArgs}"/> instead.
    /// Subclasses overriding OnEnable/OnDisable must call base.
    /// </summary>
    public abstract class AgentTool<TArgs> : MonoBehaviour, IAgentTool
    {
        [SerializeField] private ToolSet toolSet;

        public abstract string Name { get; }
        public abstract string Description { get; }
        public virtual JObject Parameters => null;

        protected abstract Awaitable<ToolResult> Run(TArgs args, CancellationToken ct);

        public Awaitable<ToolResult> Execute(JObject args, CancellationToken ct) =>
            ToolArgs.TryParse<TArgs>(args, out var parsed, out var error)
                ? Run(parsed, ct)
                : ToolResult.Completed(ToolResult.Fail(error));

        protected virtual void OnEnable()
        {
            if (toolSet != null) toolSet.Register(this);
            else Debug.LogWarning($"[AgentTool] '{Name}' on '{name}' has no ToolSet assigned.", this);
        }

        protected virtual void OnDisable()
        {
            if (toolSet != null) toolSet.Unregister(this);
        }
    }
}
