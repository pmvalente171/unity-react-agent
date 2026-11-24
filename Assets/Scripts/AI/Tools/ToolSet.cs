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
        private void OnDisable() => _tools.Clear();
    }
}
