using System;
using System.Collections.Generic;
using AI.Contract;
using UnityEngine;

namespace AI.Tools
{
    /// <summary>
    /// Registry asset that joins tools and agents without either referencing the other.
    /// Tools register while they're available (usually OnEnable/OnDisable); an Agent sends
    /// whatever is registered at each step. The list is runtime-only and never saved.
    /// </summary>
    [CreateAssetMenu(fileName = "ToolSet", menuName = "AI/Tool Set")]
    public class ToolSet : ScriptableObject
    {
        private readonly List<IAgentTool> _tools = new();
        private readonly Dictionary<object, List<IAgentTool>> _owned = new(); // RegisterFrom owner -> its tools

        public event Action Changed;

        public IReadOnlyList<IAgentTool> Tools => _tools;

        public bool Register(IAgentTool tool)
        {
            if (tool == null) return false;
            if (_tools.Contains(tool)) return true;

            if (TryGet(tool.Name, out _))
            {
                Debug.LogWarning($"[ToolSet] '{name}' already has a tool called '{tool.Name}'. Ignoring the new one.");
                return false;
            }

            _tools.Add(tool);
            Changed?.Invoke();
            return true;
        }

        public void Unregister(IAgentTool tool)
        {
            if (_tools.Remove(tool))
                Changed?.Invoke();
        }

        public void RegisterAll(IEnumerable<IAgentTool> tools)
        {
            foreach (var tool in tools) Register(tool);
        }

        public void UnregisterAll(IEnumerable<IAgentTool> tools)
        {
            foreach (var tool in tools) Unregister(tool);
        }

        /// <summary>
        /// Registers every [AgentTool] method on <paramref name="owner"/> (see <see cref="AgentToolAttribute"/>).
        /// Call from OnEnable and pair with <see cref="UnregisterFrom"/> in OnDisable. Returns how many registered.
        /// </summary>
        public int RegisterFrom(object owner)
        {
            if (owner == null) return 0;
            if (_owned.TryGetValue(owner, out var existing)) return existing.Count;

            var registered = new List<IAgentTool>();
            var tools = MethodTool.CreateAll(owner);
            foreach (var tool in tools)
                if (Register(tool)) registered.Add(tool);

            if (tools.Count == 0)
                Debug.LogWarning($"[ToolSet] {owner.GetType().Name} has no valid [AgentTool] methods.");

            _owned[owner] = registered;
            return registered.Count;
        }

        /// <summary>Removes the tools that <see cref="RegisterFrom"/> registered for <paramref name="owner"/>.</summary>
        public void UnregisterFrom(object owner)
        {
            if (owner != null && _owned.Remove(owner, out var tools))
                UnregisterAll(tools);
        }

        public bool TryGet(string toolName, out IAgentTool tool)
        {
            tool = _tools.Find(t => t.Name == toolName);
            return tool != null;
        }

        /// <summary>The tools as sent to the server in a /step request.</summary>
        public List<ToolSpec> ToSpecs()
        {
            var specs = new List<ToolSpec>(_tools.Count);
            foreach (var tool in _tools)
                specs.Add(new ToolSpec { Name = tool.Name, Description = tool.Description, Parameters = tool.Parameters });
            return specs;
        }

        // Runtime-only: don't carry stale registrations over when the asset is unloaded/reloaded.
        private void OnDisable()
        {
            _tools.Clear();
            _owned.Clear();
        }
    }
}
