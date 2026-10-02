using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CreatureExperiment.UI
{
    /// <summary>
    /// The story's Main Task: one short line in the top-right corner saying what the player should do now
    /// ("아침 먹기", "Mike를 따라 출근하기"). Never a list of steps - how to do it belongs to interaction prompts.
    ///
    /// Callers use the static API only (<see cref="SetTask"/>, <see cref="ClearTask"/>, <see cref="CompleteTask"/>,
    /// <see cref="Show"/>, <see cref="Hide"/>); the panel's objects are this component's business.
    ///
    /// A new task plays a short highlight (title reads "TASK UPDATED", panel tinted, fade-in) and settles back to
    /// "TASK". <see cref="CompleteTask"/> shows the current task struck through under "TASK COMPLETE" and then clears
    /// it; a <see cref="SetTask"/> made meanwhile is shown when that finishes. Timers use scaled time, so a pause
    /// freezes them.
    ///
    /// Separate from <see cref="DailyLife.ObjectiveHUD"/> (top-centre line owned by DailyLifeDirector, plus the
    /// shared ShowNotice channel). Hidden while <see cref="PauseMenu.IsPaused"/>; the task itself is kept.
    /// The scene UI is built by Tools ▸ CreatureExperiment ▸ Build Main Task HUD.
    /// </summary>
    public class MainTaskHUD : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform panel;
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text taskText;

        [Header("Layout")]
        [Tooltip("Gap to the screen's right / top edge, in canvas units (1920x1080 reference).")]
        [SerializeField] private Vector2 margin = new Vector2(28f, 28f);
        [Tooltip("Extra right gap in real screen pixels. The dev OnGUI panels (PlayerObservationHUD / CreatureEncounterObservationHUD) " +
                 "take the top-right 252 px; set to 0 once those debug HUDs are off.")]
        [SerializeField] private float extraRightInsetPixels = 264f;

        [Header("Look")]
        [SerializeField] private string title = "TASK";
        [SerializeField] private string updatedTitle = "TASK UPDATED";
        [SerializeField] private string completeTitle = "TASK COMPLETE";
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.45f);
        [SerializeField] private Color highlightColor = new Color(0.98f, 0.78f, 0.36f, 0.42f);
        [SerializeField] private Color titleColor = new Color(0.98f, 0.78f, 0.36f, 0.85f);
        [SerializeField] private Color taskColor = new Color(0.96f, 0.97f, 0.98f);
        [SerializeField] private Color completedTaskColor = new Color(0.75f, 0.78f, 0.82f, 0.8f);

        [Header("Timing (seconds)")]
        [SerializeField] private float fadeInSeconds = 0.25f;
        [SerializeField] private float updatedSeconds = 2f;
        [SerializeField] private float completeSeconds = 1.4f;

        [Header("Start")]
        [Tooltip("Task shown when the scene starts. Empty = nothing until a story script calls SetTask.")]
        [SerializeField] private string initialTask = "";

        [Header("Dev test (right-click the component ▸ Test …)")]
        [SerializeField] private string testTaskText = "Mike를 따라 출근하기";

        [Header("Font")]
        [Tooltip("Optional. Leave empty to build a dynamic font from an OS font with Korean glyphs (same as DialogueUI).")]
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private string[] osFontFamilies = { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR" };

        private static readonly string[] Day1Tasks =
            { "아침 먹기", "밖에서 Mike 만나기", "Mike를 따라 출근하기", "출근하기", "일하기", "퇴근하기", "귀가하기", "게임하기" };

        private static MainTaskHUD s_instance;

        private string _task = "";
        private bool _visible = true;
        private float _fade;            // 0..1, the fade-in after a new task
        private float _updatedLeft;     // > 0 while "TASK UPDATED" shows
        private float _completeLeft;    // > 0 while "TASK COMPLETE" shows
        private string _pendingTask;    // SetTask made during the completion beat
        private int _testIndex = -1;
        private Canvas _canvas;

        // ---- Static API (what story code calls) -------------------------------------------------------------

        /// <summary>The task currently on screen ("" when none).</summary>
        public static string CurrentTask => Instance != null ? Instance._task : "";

        /// <summary>Whether the HUD is allowed to show (see <see cref="Show"/> / <see cref="Hide"/>).</summary>
        public static bool IsVisible => Instance != null && Instance._visible;

        /// <summary>Show this as the main task. A different text plays the short "TASK UPDATED" highlight.</summary>
        public static void SetTask(string text) { if (Require()) Instance.SetTaskInternal(text); }

        /// <summary>No task: the panel disappears until the next <see cref="SetTask"/>.</summary>
        public static void ClearTask() { if (Require()) Instance.ClearTaskInternal(); }

        /// <summary>Optional: mark the current task done (struck through, "TASK COMPLETE"), then clear it.</summary>
        public static void CompleteTask() { if (Require()) Instance.CompleteTaskInternal(); }

        /// <summary>Let the HUD show again (a task, if any, is still there).</summary>
        public static void Show() { if (Require()) Instance._visible = true; }

        /// <summary>Hide the HUD without forgetting the task (e.g. during a cutscene).</summary>
        public static void Hide() { if (Require()) Instance._visible = false; }

        private static MainTaskHUD Instance
        {
            get
            {
                if (s_instance == null)
                    s_instance = FindFirstObjectByType<MainTaskHUD>(FindObjectsInactive.Include);
                return s_instance;
            }
        }

        private static bool Require()
        {
            if (Instance != null)
                return true;
            Debug.LogWarning("[MainTaskHUD] No MainTaskHUD in the scene (Tools ▸ CreatureExperiment ▸ Build Main Task HUD).");
            return false;
        }

        // ---- Lifecycle ----------------------------------------------------------------------------------------

        private void Awake()
        {
            if (s_instance == null)
                s_instance = this;
            _canvas = GetComponentInParent<Canvas>();

            if (font == null)
                font = CreateOsFont();
            if (font != null)
            {
                if (titleText != null) titleText.font = font;
                if (taskText != null) taskText.font = font;
            }

            ApplyIdleLook();
            if (group != null)
                group.alpha = 0f;
            if (!string.IsNullOrEmpty(initialTask) && string.IsNullOrEmpty(_task))
                SetTaskInternal(initialTask);
        }

        private void OnDestroy()
        {
            if (s_instance == this)
                s_instance = null;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime; // scaled: the pause menu freezes the beats

            if (_completeLeft > 0f)
            {
                _completeLeft -= dt;
                if (_completeLeft <= 0f)
                {
                    string next = _pendingTask;
                    _pendingTask = null;
                    _task = "";
                    if (!string.IsNullOrEmpty(next))
                        SetTaskInternal(next);
                    else
                        ApplyIdleLook();
                }
            }
            else if (_updatedLeft > 0f)
            {
                _updatedLeft -= dt;
                float t = Mathf.Clamp01(_updatedLeft / Mathf.Max(0.01f, updatedSeconds));
                if (background != null)
                    background.color = Color.Lerp(backgroundColor, highlightColor, t * t);
                if (_updatedLeft <= 0f)
                    ApplyIdleLook();
            }

            if (_fade < 1f)
                _fade = fadeInSeconds > 0f ? Mathf.Min(1f, _fade + dt / fadeInSeconds) : 1f;

            bool shown = _visible && !string.IsNullOrEmpty(_task) && !PauseMenu.IsPaused;
            if (group != null)
                group.alpha = shown ? _fade : 0f;

            if (panel != null)
            {
                float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
                panel.anchoredPosition = new Vector2(-(margin.x + extraRightInsetPixels / scale), -margin.y);
            }
        }

        // ---- Internals ----------------------------------------------------------------------------------------

        private void SetTaskInternal(string text)
        {
            text = text ?? "";
            if (string.IsNullOrEmpty(text))
            {
                ClearTaskInternal();
                return;
            }
            if (_completeLeft > 0f)
            {
                _pendingTask = text; // shown once the completion beat ends
                return;
            }
            if (text == _task)
                return;

            _task = text;
            if (taskText != null)
            {
                taskText.text = text;
                taskText.fontStyle &= ~FontStyles.Strikethrough;
                taskText.color = taskColor;
            }
            if (titleText != null)
                titleText.text = updatedTitle;
            if (background != null)
                background.color = highlightColor;
            _updatedLeft = updatedSeconds;
            _fade = 0f;
        }

        private void ClearTaskInternal()
        {
            _task = "";
            _pendingTask = null;
            _completeLeft = 0f;
            _updatedLeft = 0f;
            ApplyIdleLook();
        }

        private void CompleteTaskInternal()
        {
            if (string.IsNullOrEmpty(_task) || _completeLeft > 0f)
                return;
            _updatedLeft = 0f;
            _completeLeft = completeSeconds;
            _fade = 1f;
            if (titleText != null)
                titleText.text = completeTitle;
            if (taskText != null)
            {
                taskText.fontStyle |= FontStyles.Strikethrough;
                taskText.color = completedTaskColor;
            }
            if (background != null)
                background.color = backgroundColor;
        }

        private void ApplyIdleLook()
        {
            if (titleText != null)
            {
                titleText.text = title;
                titleText.color = titleColor;
            }
            if (taskText != null)
            {
                taskText.text = _task;
                taskText.fontStyle &= ~FontStyles.Strikethrough;
                taskText.color = taskColor;
            }
            if (background != null)
                background.color = backgroundColor;
        }

        private TMP_FontAsset CreateOsFont()
        {
            foreach (string family in osFontFamilies)
            {
                if (string.IsNullOrEmpty(family))
                    continue;
                TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(family, "Regular");
                if (asset != null)
                    return asset;
            }
            return null;
        }

        // ---- Dev test (Play Mode, component context menu) -------------------------------------------------------

        [ContextMenu("Test/Set Test Task")]
        private void TestSetTask() => SetTaskInternal(testTaskText);

        [ContextMenu("Test/Next Day 1 Task")]
        private void TestNextDay1Task()
        {
            _testIndex = (_testIndex + 1) % Day1Tasks.Length;
            SetTaskInternal(Day1Tasks[_testIndex]);
        }

        [ContextMenu("Test/Complete Task")]
        private void TestComplete() => CompleteTaskInternal();

        [ContextMenu("Test/Clear Task")]
        private void TestClear() => ClearTaskInternal();

        [ContextMenu("Test/Hide")]
        private void TestHide() => _visible = false;

        [ContextMenu("Test/Show")]
        private void TestShow() => _visible = true;
    }
}
