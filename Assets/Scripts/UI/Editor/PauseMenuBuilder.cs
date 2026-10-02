using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CreatureExperiment.UIEditor
{
    /// <summary>
    /// Builds the Tab pause menu (<see cref="CreatureExperiment.UI.PauseMenu"/>) into the open scene as ordinary scene
    /// objects: a "PauseMenu" overlay canvas with the PAUSE page and the controls reference page, plus an EventSystem
    /// if the scene has none. Re-running replaces the previous "PauseMenu" object only (and keeps the EventSystem).
    ///
    /// The controls list below is the reference shown to the player - keep it matching the real bindings in
    /// Assets/InputSystem_Actions.inputactions and the conversation rule in DialogueInput.
    /// </summary>
    public static class PauseMenuBuilder
    {
        private const string RootName = "PauseMenu";
        private const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";

        // ---- Look -----------------------------------------------------------------------------------------
        private static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.62f);
        private static readonly Color PanelColor = new Color(0.085f, 0.095f, 0.115f, 0.96f);
        private static readonly Color AccentColor = new Color(0.98f, 0.78f, 0.36f);
        private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color TextColor = new Color(0.94f, 0.95f, 0.96f);
        private static readonly Color MutedTextColor = new Color(0.62f, 0.65f, 0.70f);
        private static readonly Color KeyFaceColor = new Color(0.93f, 0.94f, 0.96f);
        private static readonly Color KeyEdgeColor = new Color(0.52f, 0.55f, 0.61f);
        private static readonly Color KeyTextColor = new Color(0.11f, 0.12f, 0.14f);
        private static readonly Color ButtonColor = new Color(0.20f, 0.22f, 0.26f);

        private const float KeySize = 56f;
        private const float KeyGap = 6f;
        private const float RowHeight = 72f;
        private const float RowGap = 10f;
        private const float KeysAreaWidth = 220f;
        private const float ColumnWidth = 640f;

        private enum Cap { Key, MouseLeft, MouseRight, Mouse, Wasd }

        private struct Row
        {
            public Cap Cap;
            public string[] Keys; // key labels for Cap.Key (several = side by side)
            public float KeyWidth;
            public string Text;
            public Row(Cap cap, string text, float keyWidth = KeySize, params string[] keys)
            { Cap = cap; Text = text; KeyWidth = keyWidth; Keys = keys; }
        }

        // ---- Controls reference (must match the real input) -------------------------------------------------
        // One "key -> what it does" list split over two columns - no per-situation sections, so no key looks
        // limited to one context. Space "대화 진행" is the user-specified label (DialogueInput currently reads E).
        private static readonly Row[] LeftRows =
        {
            new Row(Cap.Wasd, "이동"),
            new Row(Cap.Mouse, "시점 이동"),
            new Row(Cap.Key, "달리기", 104f, "Shift"),
            new Row(Cap.Key, "앉기", 84f, "Ctrl"),
            new Row(Cap.Key, "점프 / 대화 진행", 180f, "Space"),
        };

        private static readonly Row[] RightRows =
        {
            new Row(Cap.Key, "물건 집기/놓기, 상호작용, 선택", KeySize, "E"),
            new Row(Cap.MouseLeft, "아이템 사용"),
            new Row(Cap.Key, "약하게 던지기", KeySize, "F"),
            new Row(Cap.MouseRight, "강하게 던지기"),
            new Row(Cap.Key, "메뉴 열기 / 닫기", 84f, "Tab"),
        };

        private static Sprite s_rounded;

        [MenuItem("Tools/CreatureExperiment/Build Pause Menu")]
        public static void Build()
        {
            s_rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            var old = GameObject.Find(RootName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);

            // Canvas
            var root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(root, "Build Pause Menu");
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // above DialogueUI (10)
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var overlay = Stretch(Img(root.transform, "Overlay", OverlayColor, false));

            // ---- PAUSE main page
            var main = Box(overlay, "MainPage", PanelColor, new Vector2(560f, 470f));
            Label(main, "Title", "PAUSE", 64, AccentColor, FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(0f, -40f), new Vector2(560f, 84f));
            Label(main, "Subtitle", "일시정지", 24, MutedTextColor, FontStyles.Normal, TextAlignmentOptions.Center, new Vector2(0f, -122f), new Vector2(560f, 34f));
            Line(main, new Vector2(60f, -172f), new Vector2(440f, 2f));
            var resume = MenuButton(main, "Button_Resume", "계속하기", new Vector2(0f, -200f));
            var controls = MenuButton(main, "Button_Controls", "조작 방법", new Vector2(0f, -294f));
            Label(main, "Hint", "Tab  게임으로 돌아가기", 22, MutedTextColor, FontStyles.Normal, TextAlignmentOptions.Center, new Vector2(0f, -404f), new Vector2(560f, 32f));

            // ---- Controls page
            var page = Box(overlay, "ControlsPage", PanelColor, new Vector2(1440f, 790f));
            Label(page, "Title", "조작 방법", 52, AccentColor, FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(0f, -34f), new Vector2(1440f, 72f));
            Line(page, new Vector2(60f, -128f), new Vector2(1320f, 2f));
            Line(page, new Vector2(739f, -170f), new Vector2(2f, 456f));

            Column(page, "Column_Left", LeftRows, 60f, -170f);
            Column(page, "Column_Right", RightRows, 780f, -170f);

            Line(page, new Vector2(60f, -666f), new Vector2(1320f, 2f));
            var back = MenuButton(page, "Button_Back", "뒤로", new Vector2(0f, -688f), new Vector2(280f, 68f));
            Label(page, "Hint", "Tab  게임으로 돌아가기", 22, MutedTextColor, FontStyles.Normal, TextAlignmentOptions.Right, new Vector2(-60f, -706f), new Vector2(420f, 32f), alignRight: true);

            // ---- Behaviour
            var menu = Undo.AddComponent<UI.PauseMenu>(root);
            var so = new SerializedObject(menu);
            so.FindProperty("gameplayInput").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            so.FindProperty("overlay").objectReferenceValue = overlay.gameObject;
            so.FindProperty("mainPage").objectReferenceValue = main.gameObject;
            so.FindProperty("controlsPage").objectReferenceValue = page.gameObject;
            var huds = FindOnGuiBehaviours();
            var hudProp = so.FindProperty("hideWhilePaused");
            hudProp.arraySize = huds.Count;
            for (int i = 0; i < huds.Count; i++)
                hudProp.GetArrayElementAtIndex(i).objectReferenceValue = huds[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            UnityEventTools.AddPersistentListener(resume.onClick, menu.Resume);
            UnityEventTools.AddPersistentListener(controls.onClick, menu.ShowControls);
            UnityEventTools.AddPersistentListener(back.onClick, menu.ShowMain);

            page.gameObject.SetActive(false);
            overlay.gameObject.SetActive(false);

            EnsureEventSystem();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[PauseMenuBuilder] Built '{RootName}' ({huds.Count} OnGUI HUDs hidden while paused).");
        }

        // ---- Controls columns ------------------------------------------------------------------------------

        /// <summary>Rows from the top-left (x, y) of a column (no header); returns the y under the last row.</summary>
        private static float Column(RectTransform page, string name, Row[] rows, float x, float y)
        {
            // Zero-size rect at the page's top-left: children use the same (x, y) as if placed on the page.
            var section = TopLeft(new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(), page, Vector2.zero, Vector2.zero);
            foreach (var row in rows)
            {
                float h = row.Cap == Cap.Wasd ? KeySize * 2f + KeyGap : RowHeight;
                var rowRt = TopLeft(new GameObject("Row_" + row.Text, typeof(RectTransform)).GetComponent<RectTransform>(), section,
                    new Vector2(x, y), new Vector2(ColumnWidth, h));
                BuildCaps(rowRt, row, h);
                Label(rowRt, "Action", row.Text, 27, TextColor, FontStyles.Normal, TextAlignmentOptions.Left,
                    new Vector2(KeysAreaWidth + 10f, 0f), new Vector2(ColumnWidth - KeysAreaWidth - 10f, h), topLeft: true);
                y -= h + RowGap;
            }
            return y;
        }

        private static void BuildCaps(RectTransform row, Row r, float h)
        {
            float top = -(h - KeySize) * 0.5f;
            switch (r.Cap)
            {
                case Cap.Wasd:
                    Keycap(row, "W", new Vector2(KeySize + KeyGap, 0f), KeySize);
                    Keycap(row, "A", new Vector2(0f, -(KeySize + KeyGap)), KeySize);
                    Keycap(row, "S", new Vector2(KeySize + KeyGap, -(KeySize + KeyGap)), KeySize);
                    Keycap(row, "D", new Vector2(2f * (KeySize + KeyGap), -(KeySize + KeyGap)), KeySize);
                    break;
                case Cap.Mouse:
                case Cap.MouseLeft:
                case Cap.MouseRight:
                    MouseIcon(row, r.Cap, new Vector2(4f, -(h - 64f) * 0.5f));
                    break;
                default:
                    float x = 0f;
                    foreach (string k in r.Keys)
                    {
                        Keycap(row, k, new Vector2(x, top), r.KeyWidth);
                        x += r.KeyWidth + KeyGap;
                    }
                    break;
            }
        }

        /// <summary>A keycap: darker rounded base with a lighter face lifted off its bottom edge.</summary>
        private static void Keycap(RectTransform parent, string label, Vector2 topLeft, float width)
        {
            var edge = TopLeft(Img(parent, "Key_" + label, KeyEdgeColor, true), parent, topLeft, new Vector2(width, KeySize));
            var face = TopLeft(Img(edge, "Face", KeyFaceColor, true), edge, Vector2.zero, new Vector2(width, KeySize - 5f));
            Label(face, "Label", label, label.Length > 1 ? 21 : 26, KeyTextColor, FontStyles.Bold, TextAlignmentOptions.Center,
                Vector2.zero, new Vector2(width, KeySize - 5f), topLeft: true);
        }

        /// <summary>A small mouse: rounded body, split top, the pressed button in the accent colour (none for "Mouse").</summary>
        private static void MouseIcon(RectTransform parent, Cap cap, Vector2 topLeft)
        {
            const float w = 46f, hgt = 64f;
            string name = cap == Cap.MouseLeft ? "Mouse_LMB" : cap == Cap.MouseRight ? "Mouse_RMB" : "Mouse";
            var body = TopLeft(Img(parent, name, KeyFaceColor, true), parent, topLeft, new Vector2(w, hgt));
            if (cap != Cap.Mouse)
            {
                float bx = cap == Cap.MouseLeft ? 3f : w * 0.5f + 1f;
                TopLeft(Img(body, "Button", AccentColor, true), body, new Vector2(bx, -3f), new Vector2(w * 0.5f - 4f, 25f));
            }
            TopLeft(Img(body, "Split", KeyEdgeColor, false), body, new Vector2(w * 0.5f - 1f, 0f), new Vector2(2f, 28f));
            TopLeft(Img(body, "Seam", KeyEdgeColor, false), body, new Vector2(0f, -28f), new Vector2(w, 2f));
            TopLeft(Img(body, "Wheel", KeyEdgeColor, true), body, new Vector2(w * 0.5f - 3f, -7f), new Vector2(6f, 13f));
            string tag = cap == Cap.MouseLeft ? "LMB" : cap == Cap.MouseRight ? "RMB" : "Mouse";
            Label(parent, name + "_Tag", tag, 20, MutedTextColor, FontStyles.Bold, TextAlignmentOptions.Left,
                new Vector2(topLeft.x + w + 12f, topLeft.y), new Vector2(110f, hgt), topLeft: true);
        }

        // ---- UI helpers ------------------------------------------------------------------------------------

        private static RectTransform Img(Transform parent, string name, Color color, bool rounded)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            if (rounded && s_rounded != null)
            {
                img.sprite = s_rounded;
                img.type = Image.Type.Sliced;
            }
            return (RectTransform)go.transform;
        }

        private static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.GetComponent<Image>().raycastTarget = true; // swallows clicks outside the panels
            return rt;
        }

        /// <summary>Centred panel.</summary>
        private static RectTransform Box(RectTransform parent, string name, Color color, Vector2 size)
        {
            var rt = Img(parent, name, color, true);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            rt.GetComponent<Image>().raycastTarget = true;
            return rt;
        }

        /// <summary>Anchor + pivot top-left of <paramref name="parent"/>, offset down/right by (x, -y).</summary>
        private static RectTransform TopLeft(RectTransform rt, Transform parent, Vector2 pos, Vector2 size)
        {
            if (rt.parent != parent)
                rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static RectTransform Line(RectTransform parent, Vector2 topLeft, Vector2 size) =>
            TopLeft(Img(parent, "Line", LineColor, false), parent, topLeft, size);

        /// <summary>
        /// Text. Default placement: top-centre of the parent, pos.y down from the top. topLeft: top-left corner.
        /// alignRight: top-right corner, pos.x from the right edge.
        /// </summary>
        private static TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color, FontStyles style,
            TextAlignmentOptions align, Vector2 pos, Vector2 box, bool topLeft = false, bool alignRight = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Vector2 a = topLeft ? new Vector2(0f, 1f) : alignRight ? new Vector2(1f, 1f) : new Vector2(0.5f, 1f);
            rt.anchorMin = rt.anchorMax = rt.pivot = a;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = align == TextAlignmentOptions.Left ? TextAlignmentOptions.MidlineLeft
                : align == TextAlignmentOptions.Right ? TextAlignmentOptions.MidlineRight
                : TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Button MenuButton(RectTransform parent, string name, string text, Vector2 pos, Vector2? size = null)
        {
            Vector2 s = size ?? new Vector2(380f, 76f);
            var rt = Img(parent, name, Color.white, true);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = s;
            var img = rt.GetComponent<Image>();
            img.raycastTarget = true;

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = new Color(0.31f, 0.34f, 0.40f);
            colors.selectedColor = ButtonColor;
            colors.pressedColor = new Color(0.98f, 0.78f, 0.36f, 0.85f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            Label(rt, "Text", text, 30, TextColor, FontStyles.Bold, TextAlignmentOptions.Center, Vector2.zero, s, topLeft: true);
            return button;
        }

        // ---- Scene helpers ---------------------------------------------------------------------------------

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null)
                return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(es, "Build Pause Menu");
            // Input System's own default UI actions - deliberately NOT the gameplay asset the pause menu blocks.
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        /// <summary>Scene behaviours whose class declares OnGUI (the screen HUDs) - they would draw over the canvas.</summary>
        private static List<Behaviour> FindOnGuiBehaviours()
        {
            var result = new List<Behaviour>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (mb is UI.PauseMenu)
                    continue;
                for (Type t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                    if (t.GetMethod("OnGUI", flags) != null && t.Namespace != null && t.Namespace.StartsWith("CreatureExperiment"))
                    {
                        result.Add(mb);
                        break;
                    }
            }
            return result;
        }
    }
}
