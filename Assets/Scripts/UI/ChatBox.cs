using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI
{
    /// <summary>
    /// Generic UI Toolkit chat panel. Knows nothing about agents: it raises
    /// <see cref="MessageSubmitted"/> and shows whatever messages it's given.
    /// Expects the elements from ChatboxVisualTree.uxml: "messages" (ScrollView),
    /// "status" (Label), "input" (TextField), "send" (Button).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ChatBox : MonoBehaviour
    {
        private ScrollView _messages;
        private Label _status;
        private TextField _input;
        private Button _send;

        public event Action<string> MessageSubmitted;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _messages = root.Q<ScrollView>("messages");
            _status = root.Q<Label>("status");
            _input = root.Q<TextField>("input");
            _send = root.Q<Button>("send");

            if (_messages == null || _status == null || _input == null || _send == null)
            {
                Debug.LogError("[ChatBox] The UXML is missing 'messages', 'status', 'input' or 'send'.", this);
                enabled = false;
                return;
            }

            _send.clicked += Submit;
            _input.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _messages.contentContainer.RegisterCallback<GeometryChangedEvent>(OnContentResized);
            SetStatus(null);
        }

        private void OnDisable()
        {
            if (_send != null) _send.clicked -= Submit;
            _input?.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _messages?.contentContainer.UnregisterCallback<GeometryChangedEvent>(OnContentResized);
        }

        /// <summary>Adds a message. <paramref name="styleClass"/> is an extra USS class, e.g. "chat-message--user".</summary>
        public VisualElement AddMessage(string text, string sender = null, string styleClass = null)
        {
            var row = new VisualElement();
            row.AddToClassList("chat-message");
            if (!string.IsNullOrEmpty(styleClass)) row.AddToClassList(styleClass);

            if (!string.IsNullOrEmpty(sender))
            {
                var senderLabel = new Label(sender);
                senderLabel.AddToClassList("chat-message__sender");
                row.Add(senderLabel);
            }

            var body = new Label(text) { enableRichText = false }; // model text may contain <...>
            body.AddToClassList("chat-message__text");
            row.Add(body);

            _messages.Add(row);
            return row;
        }

        /// <summary>Small line above the input, e.g. "Thinking...". Null or empty hides it.</summary>
        public void SetStatus(string text)
        {
            _status.text = text ?? "";
            _status.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetInputEnabled(bool inputEnabled)
        {
            _input.SetEnabled(inputEnabled);
            _send.SetEnabled(inputEnabled);
        }

        public void Clear() => _messages.Clear();

        private void Submit()
        {
            var text = _input.value?.Trim();
            if (string.IsNullOrEmpty(text) || !_input.enabledSelf) return;

            _input.value = "";
            MessageSubmitted?.Invoke(text);
            _input.Focus();
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
            {
                Submit();
                e.StopPropagation();
            }
        }

        private void OnContentResized(GeometryChangedEvent _) =>
            _messages.scrollOffset = new Vector2(0, _messages.contentContainer.layout.height);
    }
}
