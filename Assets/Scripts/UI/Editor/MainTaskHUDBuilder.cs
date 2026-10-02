using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CreatureExperiment.UIEditor
{
    /// <summary>
    /// Builds the story Main Task panel (<see cref="CreatureExperiment.UI.MainTaskHUD"/>) into the open scene: a
    /// "MainTaskHUD" overlay canvas (below DialogueUI and PauseMenu) with one small top-right panel - accent bar,
    /// "TASK" title and the task line. Re-running replaces the previous "MainTaskHUD" object only.
    /// </summary>
    public static class MainTaskHUDBuilder
    {
        private const string RootName = "MainTaskHUD";
        private const float PanelWidth = 380f;

        private static readonly Color AccentColor = new Color(0.98f, 0.78f, 0.36f);

        [MenuItem("Tools/CreatureExperiment/Build Main Task HUD")]
        public static void Build()
        {
            var rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            var old = GameObject.Find(RootName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);

            // Canvas
            var root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(root, "Build Main Task HUD");
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5; // under DialogueUI (10) and PauseMenu (100)
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Panel: top-right, width fixed, height follows the text (a long task wraps instead of being cut).
            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var panel = (RectTransform)panelGo.transform;
            panel.SetParent(root.transform, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
            panel.sizeDelta = new Vector2(PanelWidth, 90f);
            panel.anchoredPosition = new Vector2(-28f, -28f);

            var bg = panelGo.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.45f);
            bg.raycastTarget = false;
            if (rounded != null)
            {
                bg.sprite = rounded;
                bg.type = Image.Type.Sliced;
            }

            var group = panelGo.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var layout = panelGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 20, 14, 16);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = panelGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Accent bar along the left edge (outside the layout).
            var bar = new GameObject("AccentBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            var barRt = (RectTransform)bar.transform;
            barRt.SetParent(panel, false);
            barRt.anchorMin = new Vector2(0f, 0f);
            barRt.anchorMax = new Vector2(0f, 1f);
            barRt.pivot = new Vector2(0f, 0.5f);
            barRt.offsetMin = new Vector2(8f, 12f);
            barRt.offsetMax = new Vector2(12f, -12f);
            bar.GetComponent<LayoutElement>().ignoreLayout = true;
            var barImg = bar.GetComponent<Image>();
            barImg.color = AccentColor;
            barImg.raycastTarget = false;

            var title = Text(panel, "Title", "TASK", 17f, new Color(0.98f, 0.78f, 0.36f, 0.85f), FontStyles.Bold);
            title.characterSpacing = 8f;
            var task = Text(panel, "TaskText", "", 30f, new Color(0.96f, 0.97f, 0.98f), FontStyles.Normal);
            task.lineSpacing = -6f;

            var hud = Undo.AddComponent<UI.MainTaskHUD>(root);
            var so = new SerializedObject(hud);
            so.FindProperty("group").objectReferenceValue = group;
            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("background").objectReferenceValue = bg;
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("taskText").objectReferenceValue = task;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[MainTaskHUDBuilder] Built '{RootName}'.");
        }

        private static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
