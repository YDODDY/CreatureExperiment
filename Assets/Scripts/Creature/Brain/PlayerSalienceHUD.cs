using UnityEngine;
using TMPro;

namespace CreatureExperiment.Creature.Brain
{
    /// <summary>
    /// Debug-only readout of <see cref="PlayerSalience"/>. Same shape/convention as the project's
    /// other Brain/Perception HUDs - a separate class, purely a readout, never touches the sensor it
    /// displays. Drawn bottom-left, clear of <c>ReactionEpisodeHUD</c> (top-left),
    /// <c>PlayerObservationHUD</c>/<c>CreatureEncounterObservationHUD</c> (stacked top-right) and
    /// <c>CreatureHearingHUD</c> (bottom-right).
    /// </summary>
    public class PlayerSalienceHUD : MonoBehaviour
    {
        [Tooltip("The sensor to display. Auto-found on this GameObject or its parent, else anywhere in the scene, if left empty.")]
        [SerializeField] private PlayerSalience salience;
        [Tooltip("Optional. A TextMeshPro / TextMeshProUGUI text to mirror the readout into.")]
        [SerializeField] private TMP_Text text;
        [Tooltip("Show the HUD at all. Off = fully hidden (both the TMP text, if assigned, and OnGUI).")]
        [SerializeField] private bool showDebug = true;
        [Tooltip("Also draw the readout with OnGUI (bottom-left corner) so it works with no UI set up.")]
        [SerializeField] private bool alsoDrawOnGUI = true;
        [SerializeField] private int onGuiFontSize = 14;

        private GUIStyle _style;

        private void Awake()
        {
            if (salience == null)
                salience = GetComponentInParent<PlayerSalience>();
            if (salience == null)
                salience = FindFirstObjectByType<PlayerSalience>();
        }

        private void Update()
        {
            if (!showDebug || salience == null)
            {
                if (text != null && text.gameObject.activeSelf)
                    text.text = "";
                return;
            }

            if (text != null)
                text.text = Format(salience);
        }

        private static string Format(PlayerSalience s)
        {
            string lastStim = s.LastStimulusTime >= 0f
                ? $"{s.LastStimulus} (+{s.LastStimulusBonus:F2}, {Time.time - s.LastStimulusTime,4:F1}s ago)"
                : "-";

            return
                "PLAYER SALIENCE\n\n" +
                $"Score     {s.Score,6:F2}\n" +
                $"Level     {s.AttentionLevel,10}\n" +
                $"Hold      {s.AttentionHoldRemaining,6:F1} s\n\n" +
                $"Last      {lastStim}\n\n" +
                $"VeryClose {(s.IsVeryClose ? "YES" : "no"),6}\n" +
                $"FastAppr. {(s.IsFastApproach ? "YES" : "no"),6}\n\n" +
                "STIMULI COUNT\n" +
                $"Footstep  {s.FootstepStimulusCount,4}\n" +
                $"Jump      {s.JumpStimulusCount,4}\n" +
                $"Visual    {s.VisualAcquireStimulusCount,4}\n" +
                $"ThrowHit  {s.ThrowHitStimulusCount,4}";
        }

        private void OnGUI()
        {
            if (!showDebug || !alsoDrawOnGUI || salience == null)
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

            string s = Format(salience);
            const float w = 260f;
            const float h = 300f;
            Rect rect = new Rect(12f, Screen.height - h - 12f, w, h);

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f), s, _style);
        }
    }
}
