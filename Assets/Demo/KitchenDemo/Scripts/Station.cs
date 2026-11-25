using System.Collections.Generic;
using Demo.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace Demo.KitchenDemo
{
    /// <summary>
    /// Base for every kitchen station. Registers itself so the tools find it without wiring,
    /// shows a floating label with its state, and says where the character should stand to use it.
    /// </summary>
    public abstract class Station : MonoBehaviour
    {
        private static readonly List<Station> Registered = new();

        [SerializeField] private string stationName;

        [SerializeField, Tooltip("World offset from the station to where the character stands to use it.")]
        private Vector3 standOffset = new(0f, 0f, 1.3f);

        [SerializeField] private Vector3 labelOffset = new(0f, 0.8f, 0f);

        private Label _label;
        private string _shownText;

        public static IReadOnlyList<Station> All => Registered;

        public string StationName => string.IsNullOrWhiteSpace(stationName) ? gameObject.name : stationName;
        public Vector3 StandPoint => transform.position + standOffset;

        /// <summary>Current state for the agent, e.g. "cooking chopped meat, ready in 3 s". Empty = nothing to say.</summary>
        public abstract string Status { get; }

        /// <summary>Text on the floating label.</summary>
        protected virtual string LabelText => string.IsNullOrEmpty(Status) ? StationName : $"{StationName}\n{Status}";

        public static T Find<T>() where T : Station => Registered.Find(s => s is T) as T;

        protected virtual void OnEnable() => Registered.Add(this);
        protected virtual void OnDisable() => Registered.Remove(this);

        protected virtual void Start()
        {
            if (WorldOverlay.Current != null)
                _label = WorldOverlay.Current.AddLabel(transform, labelOffset, LabelText, WorldOverlay.LabelKind.Status);
        }

        protected virtual void LateUpdate()
        {
            if (_label == null) return;
            var text = LabelText;
            if (text == _shownText) return;
            _label.text = _shownText = text;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(StandPoint, 0.3f);
        }
    }
}
