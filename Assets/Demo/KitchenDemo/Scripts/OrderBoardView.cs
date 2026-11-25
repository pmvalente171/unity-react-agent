using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Demo.KitchenDemo
{
    /// <summary>Top-left HUD panel: score, open orders, director status and a Start/Pause button.</summary>
    public sealed class OrderBoardView
    {
        private readonly VisualElement _panel;
        private readonly Label _title;
        private readonly Label _orders;
        private readonly Label _status;
        private readonly Button _toggle;

        public OrderBoardView(VisualElement root, Action onToggle)
        {
            _panel = new VisualElement { pickingMode = PickingMode.Ignore };
            var s = _panel.style;
            s.position = Position.Absolute;
            s.left = 24;
            s.top = 24;
            s.width = 380;
            s.paddingLeft = s.paddingRight = 14;
            s.paddingTop = s.paddingBottom = 10;
            s.backgroundColor = new Color(0.07f, 0.07f, 0.1f, 0.88f);
            s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = 10;

            _title = MakeLabel(18, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
            _orders = MakeLabel(17, FontStyle.Normal, Color.white);
            _orders.style.marginTop = 6;
            _status = MakeLabel(14, FontStyle.Italic, new Color(0.75f, 0.75f, 0.8f));
            _status.style.marginTop = 6;

            _toggle = new Button(onToggle) { text = "Pause orders" };
            _toggle.style.marginTop = 8;
            _toggle.style.alignSelf = Align.FlexStart;
            _toggle.style.fontSize = 15;

            _panel.Add(_title);
            _panel.Add(_orders);
            _panel.Add(_status);
            _panel.Add(_toggle);
            root.Add(_panel);
        }

        public void Refresh(string title, string orders, string status, bool running)
        {
            SetText(_title, title);
            SetText(_orders, orders);
            SetText(_status, status);
            _status.style.display = string.IsNullOrEmpty(status) ? DisplayStyle.None : DisplayStyle.Flex;
            var buttonText = running ? "Pause orders" : "Start orders";
            if (_toggle.text != buttonText) _toggle.text = buttonText;
        }

        public void Remove() => _panel.RemoveFromHierarchy();

        private static void SetText(Label label, string text)
        {
            if (label.text != text) label.text = text;
        }

        private static Label MakeLabel(int size, FontStyle style, Color color)
        {
            var label = new Label { enableRichText = false, pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.unityFontStyleAndWeight = style;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }
    }
}
