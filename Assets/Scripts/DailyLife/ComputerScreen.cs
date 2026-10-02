using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The computer's power and what its monitor shows - an old desktop OS look drawn on a World Space canvas lying on
    /// the real screen face (<see cref="screenAnchor"/>), not a browser or an OS:
    /// a teal desktop with three icons down the left (인터넷 / 채팅 / RPG게임) and a grey taskbar; each icon opens a small
    /// window with a title bar and an [X] that goes back to the desktop.
    /// - 인터넷: one fixed test page.
    /// - 채팅: fixed lines advanced with E; E after the last line closes the chat back to the desktop.
    /// - RPG게임: a test game window - "Press F to end game"; <see cref="EndGame"/> (F, read by <see cref="ComputerStation"/>)
    ///   shows THE END and raises <see cref="GameEnded"/>. No real game yet.
    ///
    /// Power is its own state, separate from "being used" (<see cref="ComputerStation"/>): off shows the monitor's
    /// own dark screen (the canvas is hidden), on shows the current page. Nothing resets it between days.
    ///
    /// There is no EventSystem in the scene, so clicks are not uGUI events: the station passes the mouse position
    /// and this tests it against the visible buttons' rects (<see cref="Hover"/> / <see cref="Click"/>). The canvas is
    /// built at runtime under the anchor; icons are plain Images (squares + a generated circle), no art assets.
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

        [Header("RPG game (test window)")]
        [SerializeField] private string gameTitle = "RPG게임";
        [SerializeField] private string gamePrompt = "Press F to end game";
        [SerializeField] private string gameEndText = "THE END";
        [SerializeField] private string taskbarClock = "오후 9:30";

        /// <summary>Raised when the RPG test game is ended (F) - the ending is on screen.</summary>
        public static event Action<ComputerScreen> GameEnded;

        private static readonly Color DesktopColor = new Color(0.0f, 0.45f, 0.47f);
        private static readonly Color FaceColor = new Color(0.75f, 0.75f, 0.75f);
        private static readonly Color LightEdge = new Color(1f, 1f, 1f);
        private static readonly Color DarkEdge = new Color(0.35f, 0.35f, 0.35f);
        private static readonly Color TitleBarColor = new Color(0.0f, 0.0f, 0.5f);
        private static readonly Color IconHover = new Color(0.1f, 0.2f, 0.75f, 0.45f);
        private static readonly Color ButtonHover = new Color(0.86f, 0.86f, 0.86f);
        private static readonly Color PageTextColor = new Color(0.08f, 0.08f, 0.08f);

        private enum Page { Desktop, Internet, Chat, Game }

        private sealed class ScreenButton
        {
            public RectTransform Rect;
            public Image Background;
            public Color Normal;
            public Color Hover;
            public Action OnClick;
        }

        private readonly List<ScreenButton> _buttons = new List<ScreenButton>();
        private GameObject _canvasRoot;
        private GameObject _internet, _chat, _game;
        private Text _chatText, _chatHint, _gameText, _gameHint;
        private Font _font;
        private Sprite _circle;
        private bool _isOn;
        private bool _interactive;
        private Page _page;
        private int _chatShown;
        private bool _gameOver;

        public bool IsOn => _isOn;

        /// <summary>The RPG window is open and the game hasn't been ended yet.</summary>
        public bool IsGameRunning => _isOn && _interactive && _page == Page.Game && !_gameOver;

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

        /// <summary>F in the RPG window: the (test) game ends - THE END is shown and <see cref="GameEnded"/> raised once.</summary>
        public void EndGame()
        {
            if (!IsGameRunning)
                return;
            _gameOver = true;
            _gameText.text = gameEndText;
            _gameText.fontSize = 110;
            _gameHint.text = "";
            GameEnded?.Invoke(this);
        }

        private ScreenButton ButtonAt(Camera cam, Vector2 screenPoint)
        {
            foreach (var b in _buttons)
                if (b.Rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(b.Rect, screenPoint, cam))
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
            if (_internet != null) _internet.SetActive(page == Page.Internet);
            if (_chat != null) _chat.SetActive(page == Page.Chat);
            if (_game != null) _game.SetActive(page == Page.Game);
            if (page == Page.Chat)
            {
                _chatShown = Mathf.Min(1, chatLines.Length);
                RefreshChat();
            }
            if (page == Page.Game)
            {
                _gameOver = false;
                _gameText.text = gamePrompt;
                _gameText.fontSize = 48;
                _gameHint.text = "E · 컴퓨터 종료";
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
            _circle = MakeCircleSprite(64);

            _canvasRoot = new GameObject("ScreenCanvas", typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)_canvasRoot.transform;
            rt.SetParent(anchor, false);
            rt.sizeDelta = screenSize * unitsPerMeter;
            rt.localScale = Vector3.one / unitsPerMeter;
            var canvas = _canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            Rect(rt, "Desktop", DesktopColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Desktop icons, top-left, one under the other.
            var icons = Rect(rt, "Icons", Color.clear, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            DesktopIcon(icons, "Icon_Internet", "인터넷", 0, InternetGlyph, () => ShowPage(Page.Internet));
            DesktopIcon(icons, "Icon_Chat", "채팅", 1, ChatGlyph, () => ShowPage(Page.Chat));
            DesktopIcon(icons, "Icon_RPG", "RPG게임", 2, SwordGlyph, () => ShowPage(Page.Game));

            // Windows (one open at a time)
            var client = Window(rt, "Window_Internet", "인터넷 - " + internetAddress, out _internet);
            var address = Rect(client, "AddressBar", Color.white, new Vector2(0f, 1f), Vector2.one, new Vector2(8f, -52f), new Vector2(-8f, -8f));
            Bevel(address, sunken: true);
            Label(address, "주소  " + internetAddress, 24, PageTextColor, TextAnchor.MiddleLeft, new Vector2(12f, 0f), Vector2.zero);
            var body = Rect(client, "PageBody", Color.white, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -60f));
            Label(body, internetTitle, 40, PageTextColor, TextAnchor.UpperLeft, new Vector2(28f, 0f), new Vector2(-28f, -20f)).fontStyle = FontStyle.Bold;
            Label(body, internetBody, 28, PageTextColor, TextAnchor.UpperLeft, new Vector2(28f, 20f), new Vector2(-28f, -90f));

            client = Window(rt, "Window_Chat", chatTitle, out _chat);
            var chatBody = Rect(client, "ChatBody", Color.white, Vector2.zero, Vector2.one, new Vector2(8f, 52f), new Vector2(-8f, -8f));
            Bevel(chatBody, sunken: true);
            _chatText = Label(chatBody, "", 30, PageTextColor, TextAnchor.UpperLeft, new Vector2(20f, 12f), new Vector2(-20f, -14f));
            _chatText.supportRichText = true;
            _chatHint = Label(client, "", 24, PageTextColor, TextAnchor.MiddleLeft, new Vector2(14f, 8f), new Vector2(-14f, -0f));
            _chatHint.rectTransform.anchorMax = new Vector2(1f, 0f);
            _chatHint.rectTransform.offsetMax = new Vector2(-14f, 48f);

            client = Window(rt, "Window_RPG", gameTitle, out _game);
            var screen = Rect(client, "GameScreen", Color.black, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -8f));
            var glyph = Rect(screen, "Emblem", Color.clear, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-40f, -150f), new Vector2(40f, -40f));
            SwordGlyph(glyph);
            _gameText = Label(screen, gamePrompt, 48, new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleCenter, new Vector2(20f, 40f), new Vector2(-20f, -130f));
            _gameText.fontStyle = FontStyle.Bold;
            _gameHint = Label(screen, "", 22, new Color(0.7f, 0.7f, 0.7f), TextAnchor.LowerRight, new Vector2(12f, 10f), new Vector2(-14f, 0f));

            // Taskbar (drawn last: always on top)
            var bar = Rect(rt, "TaskBar", FaceColor, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 52f));
            Rect(bar, "TopEdge", LightEdge, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -2f), Vector2.zero);
            var start = Rect(bar, "StartButton", FaceColor, Vector2.zero, Vector2.zero, new Vector2(6f, 6f), new Vector2(118f, 46f));
            Bevel(start, sunken: false);
            var logo = Rect(start, "Logo", Color.clear, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, -12f), new Vector2(34f, 12f));
            Rect(logo, "R", new Color(0.85f, 0.15f, 0.1f), new Vector2(0f, 0.5f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(-1f, 0f));
            Rect(logo, "G", new Color(0.15f, 0.65f, 0.2f), new Vector2(0.5f, 0.5f), Vector2.one, new Vector2(1f, 0f), Vector2.zero);
            Rect(logo, "B", new Color(0.15f, 0.3f, 0.85f), Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-1f, -1f));
            Rect(logo, "Y", new Color(0.95f, 0.8f, 0.1f), new Vector2(0.5f, 0f), new Vector2(1f, 0.5f), new Vector2(1f, 0f), new Vector2(0f, -1f));
            Label(start, "시작", 26, Color.black, TextAnchor.MiddleLeft, new Vector2(44f, 0f), Vector2.zero).fontStyle = FontStyle.Bold;
            var tray = Rect(bar, "Tray", FaceColor, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-150f, 7f), new Vector2(-6f, 45f));
            Bevel(tray, sunken: true);
            Label(tray, taskbarClock, 22, Color.black, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
            Label(bar, "LMB 클릭 · E 종료", 20, new Color(0.25f, 0.25f, 0.25f), TextAnchor.MiddleRight, new Vector2(0f, 0f), new Vector2(-170f, 0f));
        }

        // An icon cell: glyph on top, label under it; the cell highlights on hover and opens on click.
        private void DesktopIcon(RectTransform parent, string name, string label, int index, Action<RectTransform> glyph, Action onClick)
        {
            var cell = Rect(parent, name, new Color(0f, 0f, 0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -150f - index * 150f), new Vector2(148f, -18f - index * 150f));
            var g = Rect(cell, "Glyph", Color.clear, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-38f, -86f), new Vector2(38f, -10f));
            glyph(g);
            var text = Label(cell, label, 24, Color.white, TextAnchor.UpperCenter, new Vector2(0f, 0f), new Vector2(0f, -90f));
            text.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            AddButton(cell, new Color(0f, 0f, 0f, 0f), IconHover, onClick);
        }

        // A window filling most of the screen above the taskbar; returns its client area.
        private RectTransform Window(RectTransform parent, string name, string title, out GameObject root)
        {
            var frame = Rect(parent, name, FaceColor, Vector2.zero, Vector2.one, new Vector2(190f, 70f), new Vector2(-30f, -22f));
            Bevel(frame, sunken: false);
            root = frame.gameObject;
            var titleBar = Rect(frame, "TitleBar", TitleBarColor, new Vector2(0f, 1f), Vector2.one, new Vector2(4f, -44f), new Vector2(-4f, -4f));
            Label(titleBar, title, 24, Color.white, TextAnchor.MiddleLeft, new Vector2(12f, 0f), new Vector2(-60f, 0f)).fontStyle = FontStyle.Bold;
            var close = Rect(titleBar, "Close", FaceColor, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-42f, -15f), new Vector2(-6f, 15f));
            Bevel(close, sunken: false);
            Label(close, "X", 22, Color.black, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero).fontStyle = FontStyle.Bold;
            AddButton(close, FaceColor, ButtonHover, () => ShowPage(Page.Desktop));
            return Rect(frame, "Client", Color.clear, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -48f));
        }

        // ---- Glyphs (plain shapes) ------------------------------------------------------------------------

        private void InternetGlyph(RectTransform g)
        {
            Circle(g, "Globe", new Color(0.2f, 0.45f, 0.9f), Vector2.zero, Vector2.one);
            Circle(g, "LandA", new Color(0.25f, 0.7f, 0.3f), new Vector2(0.18f, 0.45f), new Vector2(0.5f, 0.8f));
            Circle(g, "LandB", new Color(0.25f, 0.7f, 0.3f), new Vector2(0.52f, 0.15f), new Vector2(0.82f, 0.48f));
            Rect(g, "Equator", new Color(1f, 1f, 1f, 0.6f), new Vector2(0.05f, 0.5f), new Vector2(0.95f, 0.5f), new Vector2(0f, -1.5f), new Vector2(0f, 1.5f));
            Rect(g, "Meridian", new Color(1f, 1f, 1f, 0.6f), new Vector2(0.5f, 0.05f), new Vector2(0.5f, 0.95f), new Vector2(-1.5f, 0f), new Vector2(1.5f, 0f));
        }

        private void ChatGlyph(RectTransform g)
        {
            var tail = Rect(g, "Tail", Color.white, new Vector2(0.2f, 0.12f), new Vector2(0.2f, 0.12f), new Vector2(-9f, -9f), new Vector2(9f, 9f));
            tail.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Circle(g, "Bubble", Color.white, new Vector2(0f, 0.12f), Vector2.one);
            for (int i = 0; i < 3; i++)
                Circle(g, "Dot" + i, new Color(0.3f, 0.3f, 0.35f), new Vector2(0.26f + i * 0.18f, 0.5f), new Vector2(0.38f + i * 0.18f, 0.62f));
        }

        private void SwordGlyph(RectTransform g)
        {
            var sword = Rect(g, "Sword", Color.clear, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            sword.localRotation = Quaternion.Euler(0f, 0f, -45f);
            Rect(sword, "Blade", new Color(0.85f, 0.88f, 0.92f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-5f, -8f), new Vector2(5f, 44f));
            Rect(sword, "Edge", new Color(1f, 1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-1f, -8f), new Vector2(1f, 44f));
            Rect(sword, "Guard", new Color(0.85f, 0.65f, 0.15f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-18f, -14f), new Vector2(18f, -8f));
            Rect(sword, "Grip", new Color(0.45f, 0.25f, 0.1f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-4f, -32f), new Vector2(4f, -14f));
            Circle(sword, "Pommel", new Color(0.85f, 0.65f, 0.15f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-7f, -42f), new Vector2(7f, -28f));
        }

        // ---- Helpers -------------------------------------------------------------------------------------

        private void AddButton(RectTransform rt, Color normal, Color hover, Action onClick)
        {
            var img = rt.GetComponent<Image>();
            if (img == null)
                img = rt.gameObject.AddComponent<Image>();
            img.color = normal;
            img.raycastTarget = false;
            _buttons.Add(new ScreenButton { Rect = rt, Background = img, Normal = normal, Hover = hover, OnClick = onClick });
        }

        // Old-style raised / sunken edge: two light and two dark 2-unit lines.
        private static void Bevel(RectTransform rt, bool sunken)
        {
            Color tl = sunken ? DarkEdge : LightEdge, br = sunken ? LightEdge : DarkEdge;
            Rect(rt, "EdgeT", tl, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -2f), Vector2.zero);
            Rect(rt, "EdgeL", tl, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(2f, 0f));
            Rect(rt, "EdgeB", br, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 2f));
            Rect(rt, "EdgeR", br, new Vector2(1f, 0f), Vector2.one, new Vector2(-2f, 0f), Vector2.zero);
        }

        private static RectTransform Rect(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
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

        private RectTransform Circle(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            var rt = Rect(parent, name, color, anchorMin, anchorMax, offsetMin, offsetMax);
            rt.GetComponent<Image>().sprite = _circle;
            return rt;
        }

        private Text Label(Transform parent, string text, int size, Color color, TextAnchor alignment, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rt = Rect(parent, "Text", Color.clear, Vector2.zero, Vector2.one, offsetMin, offsetMax);
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

        private static Sprite MakeCircleSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    byte a = (byte)(Mathf.Clamp01(r - d) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
