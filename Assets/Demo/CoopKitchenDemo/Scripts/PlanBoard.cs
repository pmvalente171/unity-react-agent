using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Demo.CoopKitchenDemo
{
    /// <summary>Notes the cooks post to plan together. The latest ones float above the board.</summary>
    public class PlanBoard : SharedStation
    {
        [SerializeField, Min(1)] private int keptNotes = 12;
        [SerializeField, Min(1)] private int shownNotes = 4;

        private readonly List<string> _notes = new();

        public override string Status => _notes.Count == 0 ? "no notes yet" : $"{_notes.Count} notes";

        protected override string LabelText => _notes.Count == 0
            ? StationName
            : $"{StationName}\n{string.Join("\n", _notes.Skip(_notes.Count - shownNotes))}";

        public void Post(string author, string text)
        {
            _notes.Add($"{author}: {text.Trim()}");
            if (_notes.Count > keptNotes) _notes.RemoveAt(0);
            MarkChanged(author);
        }

        public string Describe() => _notes.Count == 0
            ? "The plan board is empty."
            : "Plan board (oldest first):\n" + string.Join("\n", _notes.Select(n => "- " + n));
    }
}
