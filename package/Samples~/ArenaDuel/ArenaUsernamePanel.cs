using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace Pairiquium.Samples
{
    /// <summary>
    /// A full-screen gate shown before anything else in the lobby: pick a username, then (and
    /// only then) the rest of the lobby - region selection, search/cancel, and
    /// <see cref="ArenaPartyPanel"/> - becomes reachable. Built in code like
    /// <see cref="ArenaPartyPanel"/>, for the same reason: the sample scene needs no edits to gain
    /// this, and a hand-edited scene fails silently if it's ever gotten wrong (see AGENTS.md).
    ///
    /// Why this exists: <see cref="ArenaTicketProvider"/>'s userId is otherwise a random string
    /// Backend~ invents per connection - fine for matchmaking itself, useless for party invites,
    /// since nobody can read a random id off screen and type it correctly into a friend's "invite"
    /// box before it's even connected. This panel asks for a name up front and threads it through
    /// as that userId instead (see <see cref="ArenaTicketProvider.Username"/>), so "invite by
    /// username" means what it says.
    ///
    /// Blocking is just z-order: this panel is created last among the lobby's children, so uGUI
    /// raycasts hit its full-screen background before anything behind it - no need to separately
    /// disable every other control while this is up.
    /// </summary>
    internal sealed class ArenaUsernamePanel : MonoBehaviour
    {
        // Same rule Backend~/arena-backend.js enforces server-side - mirrored here so a bad name
        // is caught before a round-trip, not after.
        private static readonly Regex UsernamePattern = new Regex("^[A-Za-z0-9_-]{1,24}$");

        private ArenaTicketProvider _tickets;
        private Action _onConfirmed;
        private InputField _usernameInput;
        private Text _errorText;

        public static ArenaUsernamePanel Create(Transform lobbyCanvas, ArenaTicketProvider tickets, Action onConfirmed)
        {
            var go = new GameObject("UsernameGatePanel", typeof(RectTransform));
            go.transform.SetParent(lobbyCanvas, false);
            var panel = go.AddComponent<ArenaUsernamePanel>();
            panel._tickets = tickets;
            panel._onConfirmed = onConfirmed;
            panel.Build();
            return panel;
        }

        private void Build()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            // Covers the whole canvas and is the last sibling built, so it sits on top of and
            // blocks raycasts to every other lobby control until Confirm() hides it.
            var background = gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.85f);

            var box = new GameObject("Box", typeof(RectTransform));
            box.transform.SetParent(transform, false);
            var boxRect = (RectTransform)box.transform;
            boxRect.anchorMin = boxRect.anchorMax = boxRect.pivot = new Vector2(0.5f, 0.5f);
            boxRect.sizeDelta = new Vector2(360f, 0f);

            // An opaque card, not just the dimmed full-screen background - this sample's own
            // lobby background happens to be plain black, so a semi-transparent box alone would
            // be invisible against it.
            var boxBackground = box.AddComponent<Image>();
            boxBackground.color = new Color(0.14f, 0.16f, 0.2f, 1f);

            var layout = box.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 8f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = box.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            MakeText("Choose a username", 18, Color.white, box.transform, bold: true);
            MakeText(
                "Other players use this to invite you to a party.",
                13, new Color(0.8f, 0.8f, 0.8f), box.transform);

            _usernameInput = MakeInput("Username", box.transform);
            _errorText = MakeText(string.Empty, 13, new Color(1f, 0.4f, 0.4f), box.transform);

            Button confirm = MakeButton("Enter lobby", Confirm, box.transform);
            confirm.gameObject.AddComponent<LayoutElement>().minHeight = 32f;
        }

        private void Confirm()
        {
            string name = _usernameInput.text.Trim();
            if (!UsernamePattern.IsMatch(name))
            {
                _errorText.text = "1-24 characters: letters, numbers, - or _ only.";
                return;
            }

            _tickets.Username = name;
            gameObject.SetActive(false);
            _onConfirmed?.Invoke();
        }

        // --- Tiny uGUI helpers, deliberately duplicated rather than shared with ArenaPartyPanel -
        // each sample script here is self-contained on purpose (see AGENTS.md on why these samples
        // avoid a shared-utility layer that would obscure what belongs to the SDK itself). ---

        private static Font _font;

        private static Font DefaultFont()
        {
            if (_font == null)
            {
#if UNITY_2022_2_OR_NEWER
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            }

            return _font;
        }

        private Text MakeText(string text, int size, Color color, Transform parent, bool bold = false)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.font = DefaultFont();
            label.fontSize = size;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.color = color;
            label.text = text;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }

        private Button MakeButton(string label, Action onClick, Transform parent)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.25f, 0.35f, 0.5f, 1f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            Text text = MakeText(label, 14, Color.white, go.transform);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            return button;
        }

        private InputField MakeInput(string placeholder, Transform parent)
        {
            var go = new GameObject(placeholder, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.9f);
            go.AddComponent<LayoutElement>().minHeight = 28f;
            var input = go.AddComponent<InputField>();
            input.targetGraphic = image;
            input.characterLimit = 24;

            Text text = MakeText(string.Empty, 14, Color.black, go.transform);
            Text hint = MakeText(placeholder, 14, new Color(0.35f, 0.35f, 0.35f), go.transform);
            hint.fontStyle = FontStyle.Italic;
            foreach (Text t in new[] { text, hint })
            {
                t.alignment = TextAnchor.MiddleLeft;
                var r = (RectTransform)t.transform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(6f, 0f);
                r.offsetMax = new Vector2(-6f, 0f);
            }

            input.textComponent = text;
            input.placeholder = hint;
            return input;
        }
    }
}
