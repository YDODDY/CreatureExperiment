using UnityEngine;
using TMPro;

namespace CreatureExperiment.Creature.Brain
{
    /// <summary>
    /// Debug-only readout of <see cref="PlayerObservation"/>, so its numbers can be sanity-checked
    /// live in the DailyLife scene without a debugger. Same shape as <c>ObjectiveHUD</c>: an optional
    /// <see cref="TMP_Text"/> mirror, plus an independent OnGUI fallback so the numbers are visible
    /// with zero UI setup. Purely a readout - it does not touch <see cref="PlayerObservation"/> or
    /// any Creature system.
    /// </summary>
    public class PlayerObservationHUD : MonoBehaviour
    {
        [Tooltip("The sensor to display. Auto-found on this GameObject or its parent if left empty.")]
        [SerializeField] private PlayerObservation observation;
        [Tooltip("Optional. A TextMeshPro / TextMeshProUGUI text to mirror the readout into.")]
        [SerializeField] private TMP_Text text;
        [Tooltip("Show the HUD at all. Off = fully hidden (both the TMP text, if assigned, and OnGUI).")]
        [SerializeField] private bool showDebug = true;
        [Tooltip("Also draw the readout with OnGUI (top-right corner) so it works with no UI set up.")]
        [SerializeField] private bool alsoDrawOnGUI = true;
        [SerializeField] private int onGuiFontSize = 14;

        private GUIStyle _style;

        private void Awake()
        {
            if (observation == null)
                observation = GetComponentInParent<PlayerObservation>();
            if (observation == null)
                observation = FindFirstObjectByType<PlayerObservation>();
        }

        private void Update()
        {
            if (!showDebug || observation == null)
            {
                if (text != null && text.gameObject.activeSelf)
                    text.text = "";
                return;
            }

            string s = Format(observation);
            if (text != null)
                text.text = s;
        }

        private static string Format(PlayerObservation o)
        {
            string state = o.IsHighViewActive ? "HIGH"
                : o.IsLowViewActive ? "LOW"
                : o.IsLowViewCandidate ? "LOW?"
                : "NORMAL";
            string warmup = o.IsBaselineWarmedUp ? "" : "  (warmup)";

            return
                "PLAYER OBSERVATION\n\n" +
                "MOVE\n" +
                $"Current   {o.CurrentMoveSpeed,6:F2} m/s\n" +
                $"Average   {o.AverageMoveSpeed,6:F2} m/s\n" +
                $"Still     {o.StillRatio * 100f,6:F0} %\n\n" +
                "VIEW\n" +
                $"Current   {o.CurrentViewAngularSpeed,6:F0} deg/s\n" +
                $"Baseline  {o.BaselineViewAngularSpeed,6:F0} deg/s\n" +
                $"Short     {o.ShortViewAngularSpeed,6:F0} deg/s\n" +
                $"Deviation {o.ViewDeviationRatio,6:F2}x\n\n" +
                $"High      {o.HighViewEpisodeCount,6}\n" +
                $"High Peak {o.AverageHighViewPeak,6:F0} deg/s\n\n" +
                $"Low       {o.LowViewEpisodeCount,6}\n" +
                $"State     {state,6}{warmup}";
        }

        private void OnGUI()
        {
            if (!showDebug || !alsoDrawOnGUI || observation == null)
                return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = onGuiFontSize,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false,
                };
                _style.normal.textColor = Color.white;
            }

            string s = Format(observation);
            const float w = 240f;
            const float h = 300f;
            Rect rect = new Rect(Screen.width - w - 12f, 12f, w, h);

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f), s, _style);
        }
    }
}
