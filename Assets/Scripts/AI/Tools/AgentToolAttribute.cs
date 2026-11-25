using System;
using JetBrains.Annotations;

namespace AI.Tools
{
    /// <summary>
    /// Marks a method as a tool the agent can call. Register the owning object with
    /// <see cref="ToolSet.RegisterFrom"/> (usually in OnEnable) and unregister it with
    /// <see cref="ToolSet.UnregisterFrom"/> (in OnDisable).
    /// <code>
    /// [AgentTool, Description("Walk to a named place in the level.")]
    /// async Awaitable&lt;ToolResult&gt; MoveTo([Description("Where to go")] Place place,
    ///                                     bool run = false, CancellationToken ct = default) { ... }
    /// </code>
    /// <list type="bullet">
    /// <item>Name: the method name in snake_case without "Async" (MoveTo → move_to), unless given.</item>
    /// <item>Description: <see cref="System.ComponentModel.DescriptionAttribute"/> on the method and its parameters.</item>
    /// <item>Parameters: string, bool, integer and floating-point types, enums, arrays/lists of those,
    /// nullable versions, or a JObject. Parameters with a default value (or nullable) are optional.
    /// A CancellationToken parameter is filled in by the agent and not shown to the model.</item>
    /// <item>Returns: ToolResult, string or void, or Awaitable / Awaitable&lt;ToolResult&gt; / Awaitable&lt;string&gt;.
    /// A string means success; return ToolResult.Fail for expected failures.</item>
    /// </list>
    /// Unsupported signatures are logged as errors when the owner is registered.
    /// For nested argument objects or names built at runtime, use <see cref="DelegateTool{TArgs}"/>.
    /// IL2CPP builds with high code stripping may also need [UnityEngine.Scripting.Preserve] on the method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = true)]
    [MeansImplicitUse] // tells Rider the method is used even though nothing calls it directly
    public sealed class AgentToolAttribute : Attribute
    {
        /// <summary>Overrides the tool name; null uses the method name in snake_case.</summary>
        public string Name { get; }

        public AgentToolAttribute(string name = null)
        {
            Name = name;
        }
    }
}
