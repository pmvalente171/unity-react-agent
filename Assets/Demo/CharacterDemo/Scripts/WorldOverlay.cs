using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Demo.CharacterDemo
{
    /// <summary>
    /// Screen-space UI Toolkit labels that follow world objects (place names, speech bubbles).
    /// Needs a UIDocument with PanelSettings and no UXML; it builds everything in code.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class WorldOverlay : MonoBehaviour
    {
        public enum LabelKind
        {
            Place,
            Speech,
        }

        private sealed class Entry
        {
            public Transform Target;
            public Vector3 Offset;
            public VisualElement Element;
        }

        private readonly List<Entry> _entries = new();
        private VisualElement _root;
        private Camera _camera;

        /// <summary>The active overlay, or null. One per scene is enough.</summary>
        public static WorldOverlay Current { get; private set; }

        private void OnEnable()
        {
            Current = this;
            _root = GetComponent<UIDocument>().rootVisualElement;
            _root.pickingMode = PickingMode.Ignore; // never block clicks on the chat
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
            foreach (var entry in _entries) entry.Element.RemoveFromHierarchy();
            _entries.Clear();
        }

        public Label AddLabel(Transform target, Vector3 offset, string text, LabelKind kind)
        {
            var label = new Label(text) { enableRichText = false, pickingMode = PickingMode.Ignore };
            Style(label, kind);
            _root.Add(label);
            var entry = new Entry { Target = target, Offset = offset, Element = label };
            _entries.Add(entry);
            Place(entry);
            return label;
        }

        public void Remove(VisualElement element)
        {
            if (element == null) return;
            element.RemoveFromHierarchy();
            _entries.RemoveAll(e => e.Element == element);
        }

        private void LateUpdate()
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Target == null)
                {
                    _entries[i].Element.RemoveFromHierarchy();
                    _entries.RemoveAt(i);
                    continue;
                }

                Place(_entries[i]);
            }
        }

        private void Place(Entry entry)
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || _root.panel == null) return;

            var world = entry.Target.position + entry.Offset;
            var inFront = _camera.WorldToViewportPoint(world).z > 0;
            entry.Element.style.display = inFront ? DisplayStyle.Flex : DisplayStyle.None;
            if (!inFront) return;

            var point = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, world, _camera);
            entry.Element.style.left = point.x;
            entry.Element.style.top = point.y;
        }

        private static void Style(Label label, LabelKind kind)
        {
            var s = label.style;
            s.position = Position.Absolute;
            // Anchor at the bottom-centre of the label, so it sits on top of the point.
            s.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-100, LengthUnit.Percent));
            s.paddingLeft = s.paddingRight = 8;
            s.paddingTop = s.paddingBottom = 3;
            s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = 6;
            s.unityTextAlign = TextAnchor.MiddleCenter;

            if (kind == LabelKind.Place)
            {
                s.fontSize = 18;
                s.color = new Color(1f, 1f, 1f, 0.9f);
                s.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            }
            else
            {
                s.fontSize = 20;
                s.maxWidth = 360;
                s.whiteSpace = WhiteSpace.Normal;
                s.color = new Color(0.1f, 0.1f, 0.12f);
                s.backgroundColor = new Color(1f, 1f, 1f, 0.95f);
            }
        }
    }
}
