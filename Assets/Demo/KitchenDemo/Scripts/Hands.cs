using Demo.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace Demo.KitchenDemo
{
    /// <summary>
    /// What the character carries: one item at a time. Shows it as a small coloured block in
    /// front of the character and a label above its head.
    /// </summary>
    public class Hands : MonoBehaviour
    {
        [SerializeField] private Vector3 holdOffset = new(0f, 0.1f, 0.65f);
        [SerializeField] private Vector3 labelOffset = new(0f, 1.4f, 0f);

        private GameObject _view;
        private Material _viewMaterial;
        private Label _label;

        public IHoldable Held { get; private set; }
        public bool IsEmpty => Held == null;
        public string Describe() => Held == null ? "nothing" : Held.Name;

        public void Hold(IHoldable item)
        {
            Held = item;
            Refresh();
        }

        public IHoldable Release()
        {
            var item = Held;
            Held = null;
            Refresh();
            return item;
        }

        /// <summary>Call after the held item changes (e.g. it was chopped).</summary>
        public void Refresh()
        {
            if (Held == null)
            {
                if (_view != null) _view.SetActive(false);
                SetLabel(null);
                return;
            }

            EnsureView();
            _view.SetActive(true);
            _view.transform.localScale = Held is Plate ? new Vector3(0.6f, 0.06f, 0.6f) : new Vector3(0.3f, 0.3f, 0.3f);
            _viewMaterial.color = Held.Color;
            SetLabel(Held.Name);
        }

        private void EnsureView()
        {
            if (_view != null) return;

            // Copy the character's material so the block renders with the project's pipeline.
            var source = GetComponent<Renderer>();

            _view = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _view.name = "Held item";
            Destroy(_view.GetComponent<Collider>());
            _view.transform.SetParent(transform, false);
            _view.transform.localPosition = holdOffset;

            _viewMaterial = source != null ? new Material(source.sharedMaterial) : _view.GetComponent<Renderer>().material;
            _view.GetComponent<Renderer>().sharedMaterial = _viewMaterial;
        }

        private void SetLabel(string text)
        {
            var overlay = WorldOverlay.Current;
            if (overlay == null) return;

            if (string.IsNullOrEmpty(text))
            {
                overlay.Remove(_label);
                _label = null;
            }
            else if (_label == null)
                _label = overlay.AddLabel(transform, labelOffset, text, WorldOverlay.LabelKind.Status);
            else
                _label.text = text;
        }

        private void OnDestroy()
        {
            if (_viewMaterial != null) Destroy(_viewMaterial);
        }
    }
}
