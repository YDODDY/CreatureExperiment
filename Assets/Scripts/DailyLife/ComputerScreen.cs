using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The computer's power and what its monitor shows - a small fixed set of pages drawn on a World Space
    /// canvas lying on the real screen face (<see cref="screenAnchor"/>), not a browser or an OS:
    /// Desktop (인터넷 / 채팅 icons) → Internet (one fixed test page) or Chat (fixed lines advanced with E; E after the
    /// last line closes the chat back to the desktop).
    ///
    /// Power is its own state, separate from "being used" (<see cref="ComputerStation"/>): off shows the monitor's
    /// own dark screen (the canvas is hidden), on shows the current page. Nothing resets it between days.
    ///
    /// There is no EventSystem in the scene, so clicks are not uGUI events: the station passes the mouse position
    /// and this tests it against the visible buttons' rects (<see cref="Hover"/> / <see cref="Click"/>). The canvas is
    /// built at runtime under the anchor; the page texts below are placeholder content meant to be swapped later.
    /// </summary>
    public class ComputerScreen : MonoBehaviour
    {
        [Header("Screen")]
        [Tooltip("Centre of the screen face, just in front of it; forward points INTO the screen (the viewer looks along it).")]
        [SerializeField] private Transform screenAnchor;
        [Tooltip("Visible screen size in metres (width, height).")]
        [SerializeField] private Vector2 screenSize = new Vector2(0.6f, 0.355f);
        [Tooltip("Canvas units per metre - sets the text sharpness.")]
        [SerializeField] private float unitsPerMeter = 2000f;
        [SerializeField] private bool poweredOnAtStart;

        [Header("Internet test page (placeholder)")]
        [SerializeField] private string internetAddress = "http://test.page";
        [SerializeField] private string internetTitle = "테스트 페이지";
        [TextArea(3, 8)]
        [SerializeField] private string internetBody = "이 페이지는 컴퓨터 0.1 테스트용 임시 페이지입니다.\n\n나중에 실제 사이트 내용으로 교체됩니다.";

        [Header("Chat test (placeholder)")]
        [SerializeField] private string chatTitle = "채팅 · 테스트";
        [SerializeField] private string chatSpeaker = "상대";
        [SerializeField] private string[] chatLines = { "안녕하세요. 테스트 메시지입니다.", "E로 다음 줄이 나옵니다.", "테스트 대화는 여기까지입니다." };

        private static readonly Color BackgroundColor = new Color(0.09f, 0.2f, 0.3f);
        private static readonly Color BarColor = new Color(0.05f, 0.07f, 0.09f);
        private static readonly Color IconColor = new Color(0.18f, 0.36f, 0.5f);
        private static readonly Color IconHoverColor = new Color(0.3f, 0.55f, 0.72f);
        private static readonly Color ButtonColor = new Color(0.25f, 0.28f, 0.32f);
        private static readonly Color ButtonHoverColor = new Color(0.4f, 0.45f, 0.5f);
        private static readonly Color PageColor = new Color(0.95f, 0.95f, 0.93f);
        private static readonly Color PageTextColor = new Color(0.12f, 0.12f, 0.12f);

        private enum Page { Desktop, Internet, Chat }

        private sealed class ScreenButton
        {
            public RectTransform Rect;
            public Image Background;
            public Color Normal;
            public Color Hover;
            public GameObject Page;
            public Action OnClick;
        }

        private readonly List<ScreenButton> _buttons = new List<ScreenButton>();
        private GameObject _canvasRoot;
        private GameObject _desktop, _internet, _chat;
        private Text _chatText, _chatHint;
        private Font _font;
        private bool _isOn;
        private bool _interactive;
        private Page _page;
        private int _chatShown;

        public bool IsOn => _isOn;

        private void Awake()
        {
            BuildCanvas();
            _isOn = poweredOnAtStart;
            ShowPage(Page.Desktop);
            ApplyPower();
        }

        public void SetPower(bool on)
        {
            _isOn = on;
            ApplyPower();
        }

        /// <summary>Back to the desktop (each time the computer is sat down at).</summary>
        public void ShowDesktop() => ShowPage(Page.Desktop);

        /// <summary>While false (nobody using it) nothing is highlighted.</summary>
        public void SetInteractive(bool interactive)
        {
            _interactive = interactive;
            if (!interactive)
                Hover(null, Vector2.zero);
        }

        /// <summary>Highlight the button under <paramref name="screenPoint"/> (pass a null camera to clear).</summary>
        public void Hover(Camera cam, Vector2 screenPoint)
        {
            ScreenButton over = cam != null && _interactive ? ButtonAt(cam, screenPoint) : null;
            foreach (var b in _buttons)
                b.Background.color = b == over ? b.Hover : b.Normal;
        }

        public void Click(Camera cam, Vector2 screenPoint)
        {
            if (!_isOn || !_interactive || cam == null)
                return;
            ButtonAt(cam, screenPoint)?.OnClick?.Invoke();
        }

        /// <summary>
        /// E on the screen: on the chat page, the next line - or, after the last line, back to the desktop. True if the
        /// press was used here; false off the chat page (the station then leaves the computer).
        /// </summary>
        public bool AdvanceChat()
        {
            if (!_isOn || !_interactive || _page != Page.Chat)
                return false;
            if (_chatShown < chatLines.Length)
            {
                _chatShown++;
                RefreshChat();
            }
            else
            {
                ShowPage(Page.Desktop);
            }
            return true;
        }

        private ScreenButton ButtonAt(Camera cam, Vector2 screenPoint)
        {
            foreach (var b in _buttons)
                if (b.Page.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(b.Rect, screenPoint, cam))
                    return b;
            return null;
        }

        private void ApplyPower()
        {
            if (_canvasRoot != null)
                _canvasRoot.SetActive(_isOn);
        }

        private void ShowPage(Page page)
        {
            _page = page;
            if (_desktop != null) _desktop.SetActive(page == Page.Desktop);
            if (_internet != null) _internet.SetActive(page == Page.Internet);
            if (_chat != null) _chat.SetActive(page == Page.Chat);
            if (page == Page.Chat)
            {
                _chatShown = Mathf.Min(1, chatLines.Length);
                RefreshChat();
            }
        }

        private void RefreshChat()
        {
            if (_chatText == null)
                return;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _chatShown; i++)
                sb.Append("<b>").Append(chatSpeaker).Append("</b>  ").Append(chatLines[i]).Append("\n\n");
            _chatText.text = sb.ToString();
            _chatHint.text = _chatShown < chatLines.Length ? "E · 다음" : "E · 대화 닫기";
        }

        // ---- Canvas construction ------------------------------------------------------------------------

        private void BuildCanvas()
        {
            Transform anchor = screenAnchor != null ? screenAnchor : transform;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _canvasRoot = new GameObject("ScreenCanvas", typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)_canvasRoot.transform;
            rt.SetParent(anchor, false);
            rt.sizeDelta = screenSize * unitsPerMeter;
            rt.localScale = Vector3.one / unitsPerMeter;
            var canvas = _canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            Panel(rt, "Background", BackgroundColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Task bar with the controls, on every page.
            var bar = Panel(rt, "TaskBar", BarColor, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 56f));
            Label(bar, "Computer 0.1", 26, Color.white, TextAnchor.MiddleLeft, new Vector2(24f, 0f), Vector2.zero);
            Label(bar, "LMB 클릭 · E 진행 / 종료", 26, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleRight, Vector2.zero, new Vector2(-24f, 0f));

            var content = Panel(rt, "Pages", Color.clear, Vector2.zero, Vector2.one, new Vector2(0f, 56f), Vector2.zero);

            // Desktop
            _desktop = Panel(content, "Desktop", Color.clear, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            Label(_desktop.transform, "바탕화면", 36, Color.white, TextAnchor.UpperLeft, new Vector2(32f, 0f), new Vector2(0f, -24f));
            Button(_desktop, "Icon_Internet", "인터넷", 44, IconColor, IconHoverColor,
                new Vector2(0.5f, 0.5f), new Vector2(-170f, -10f), new Vector2(280f, 220f), () => ShowPage(Page.Internet));
            Button(_desktop, "Icon_Chat", "채팅", 44, IconColor, IconHoverColor,
                new Vector2(0.5f, 0.5f), new Vector2(170f, -10f), new Vector2(280f, 220f), () => ShowPage(Page.Chat));

            // Internet test page
            _internet = Panel(content, "Internet", Color.clear, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            var address = Panel(_internet.transform, "AddressBar", new Color(0.82f, 0.84f, 0.86f), new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -64f), Vector2.zero);
            Label(address, internetAddress, 28, PageTextColor, TextAnchor.MiddleLeft, new Vector2(24f, 0f), Vector2.zero);
            var page = Panel(_internet.transform, "PageBody", PageColor, Vector2.zero, Vector2.one, new Vector2(0f, 0f), new Vector2(0f, -64f));
            Label(page, internetTitle, 44, PageTextColor, TextAnchor.UpperLeft, new Vector2(40f, 0f), new Vector2(-40f, -28f)).fontStyle = FontStyle.Bold;
            Label(page, internetBody, 30, PageTextColor, TextAnchor.UpperLeft, new Vector2(40f, 110f), new Vector2(-40f, -100f));
            Button(_internet, "Back", "돌아가기", 30, ButtonColor, ButtonHoverColor,
                new Vector2(1f, 0f), new Vector2(-140f, 52f), new Vector2(220f, 64f), () => ShowPage(Page.Desktop));

            // Chat test
            _chat = Panel(content, "Chat", Color.clear, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            var chatHeader = Panel(_chat.transform, "Header", BarColor, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -64f), Vector2.zero);
            Label(chatHeader, chatTitle, 32, Color.white, TextAnchor.MiddleLeft, new Vector2(24f, 0f), Vector2.zero);
            _chatText = Label(_chat.transform, "", 32, Color.white, TextAnchor.UpperLeft, new Vector2(40f, 110f), new Vector2(-40f, -90f));
            _chatText.supportRichText = true;
            _chatHint = Label(_chat.transform, "", 28, new Color(0.75f, 0.85f, 0.95f), TextAnchor.LowerLeft, new Vector2(40f, 28f), new Vector2(-300f, 0f));
            Button(_chat, "Back", "돌아가기", 30, ButtonColor, ButtonHoverColor,
                new Vector2(1f, 0f), new Vector2(-140f, 52f), new Vector2(220f, 64f), () => ShowPage(Page.Desktop));
        }

        private static RectTransform Panel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            if (color.a > 0f)
            {
                var img = go.AddComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
            }
            return rt;
        }

        private Text Label(Transform parent, string text, int size, Color color, TextAnchor alignment, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rt = Panel(parent, "Text", Color.clear, Vector2.zero, Vector2.one, offsetMin, offsetMax);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = alignment;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }

        private void Button(GameObject page, string name, string text, int size, Color normal, Color hover,
            Vector2 anchor, Vector2 position, Vector2 buttonSize, Action onClick)
        {
            var rt = Panel(page.transform, name, normal, anchor, anchor, Vector2.zero, Vector2.zero);
            rt.sizeDelta = buttonSize;
            rt.anchoredPosition = position;
            Label(rt, text, size, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
            _buttons.Add(new ScreenButton
            {
                Rect = rt,
                Background = rt.GetComponent<Image>(),
                Normal = normal,
                Hover = hover,
                Page = page,
                OnClick = onClick,
            });
        }
    }
}
