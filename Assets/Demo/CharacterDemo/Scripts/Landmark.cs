using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demo.CharacterDemo
{
    /// <summary>
    /// A named place the character can walk to. Drop it on any object to add a place:
    /// the tools pick it up automatically, nothing else to wire.
    /// </summary>
    public class Landmark : MonoBehaviour
    {
        private static readonly List<Landmark> Registered = new();

        [SerializeField, Tooltip("Name the agent uses, e.g. \"kitchen\". Empty = GameObject name.")]
        private string placeName;

        [SerializeField, TextArea] private string description;
        [SerializeField, Min(0.5f)] private float radius = 2.5f;

        public static IReadOnlyList<Landmark> All => Registered;

        public string PlaceName => string.IsNullOrWhiteSpace(placeName) ? gameObject.name : placeName.Trim();
        public string Description => description;
        public float Radius => radius;

        /// <summary>Is <paramref name="position"/> inside this place (ignoring height)?</summary>
        public bool Contains(Vector3 position) => FlatDistance(position) <= radius;

        public float FlatDistance(Vector3 position)
        {
            var d = position - transform.position;
            d.y = 0;
            return d.magnitude;
        }

        /// <summary>Case-insensitive lookup; also accepts "the kitchen", "Kitchen" or "dining_room" for "dining room".</summary>
        public static Landmark Find(string name)
        {
            var wanted = Normalize(name);
            return Registered.Find(l => Normalize(l.PlaceName) == wanted);
        }

        private static string Normalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var n = name.Trim().Replace('_', ' ').ToLowerInvariant();
            return n.StartsWith("the ", StringComparison.Ordinal) ? n.Substring(4) : n;
        }

        private void OnEnable() => Registered.Add(this);
        private void OnDisable() => Registered.Remove(this);

        private void Start()
        {
            if (WorldOverlay.Current != null)
                WorldOverlay.Current.AddLabel(transform, Vector3.up * 0.1f, PlaceName, WorldOverlay.LabelKind.Place);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.identity, new Vector3(1, 0.01f, 1));
            Gizmos.DrawWireSphere(Vector3.zero, radius);
        }
    }
}
