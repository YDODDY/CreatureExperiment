using UnityEngine;
using TMPro;

namespace CreatureExperiment.Creature.Brain
{
    /// <summary>
    /// Debug-only readout of <see cref="CreatureEncounterObservation"/>. Kept as its own HUD class,
    /// separate from <see cref="PlayerObservationHUD"/> (which stays exactly as it was), the same way
    /// <see cref="CreatureEncounterObservation"/> is kept separate from <see cref="PlayerObservation"/>
    /// - one sensor, one readout, sensor code never touches HUD code. Drawn as a second panel directly
    /// below <c>PlayerObservationHUD</c>'s (same corner, stacked) rather than merged into one box, so
    /// either can be added/removed/repositioned independently. Purely a readout - it does not touch
    /// <see cref="CreatureEncounterObservation"/> or any Creature system.
    /// </summary>
    public class CreatureEncounterObservationHUD : MonoBehaviour
    {
        [Tooltip("The sensor to display. Auto-found on this GameObject or its parent, else anywhere in the scene, if left empty.")]
        [SerializeField] private CreatureEncounterObservation encounter;
        [Tooltip("Optional. A TextMeshPro / TextMeshProUGUI text to mirror the readout into.")]
        [SerializeField] private TMP_Text text;
        [Tooltip("Show the HUD at all. Off = fully hidden (both the TMP text, if assigned, and OnGUI).")]
        [SerializeField] private bool showDebug = true;
        [Tooltip("Also draw the readout with OnGUI (top-right corner, below PlayerObservationHUD's panel) so it works with no UI set up.")]
        [SerializeField] private bool alsoDrawOnGUI = true;
        [SerializeField] private int onGuiFontSize = 14;
        [Tooltip("Vertical pixel offset of this panel's top edge - set above PlayerObservationHUD's panel height so the two stack without overlapping.")]
        [SerializeField] private float onGuiTopOffset = 320f;

        private GUIStyle _style;

        private void Awake()
        {
            if (encounter == null)
                encounter = GetComponentInParent<CreatureEncounterObservation>();
            if (encounter == null)
                encounter = FindFirstObjectByType<CreatureEncounterObservation>();
        }

        private void Update()
        {
            if (!showDebug || encounter == null)
            {
                if (text != null && text.gameObject.activeSelf)
                    text.text = "";
                return;
            }

            if (text != null)
                text.text = Format(encounter);
        }

        private static string Format(CreatureEncounterObservation e)
        {
            string inRange = e.IsCreatureInObservationRange ? "YES" : "NO";
            string looking = e.IsLookingAtCreature ? "YES" : "NO";
            string visible = e.IsCreatureVisibleInFOV ? "YES" : "NO";
            string moveState = e.CurrentRelativeMoveState.ToString().ToUpperInvariant();
            string dist = float.IsInfinity(e.DistanceToCreature) ? "  --  " : $"{e.DistanceToCreature,6:F2}";

            return
                "CREATURE ENCOUNTER\n\n" +
                $"Distance  {dist} m\n" +
                $"In Range  {inRange,6}\n" +
                $"View Angle{e.CreatureViewAngle,6:F0} deg\n" +
                $"Looking   {looking,6}\n\n" +
                $"Exposure  {e.CurrentExposureDuration,6:F1} s\n" +
                $"Gaze Count{e.GazeCount,6}\n" +
                $"Gaze Now  {e.CurrentGazeDuration,6:F1} s\n" +
                $"Gaze Total{e.TotalGazeTime,6:F1} s\n" +
                $"Gaze Ratio{e.GazeRatio * 100f,6:F0} %\n" +
                $"Longest   {e.LongestGazeDuration,6:F1} s\n\n" +
                $"Toward Spd{e.TowardCreatureSpeed,6:F2} m/s\n" +
                $"Move State{moveState,6}\n\n" +
                "VISUAL (Player camera, range-independent)\n" +
                $"Visible   {visible,6}\n" +
                $"CenterOff {e.ScreenCenterOffset,6:F2}\n" +
                $"Exposure  {e.CurrentVisualExposureDuration,6:F1} s\n" +
                $"Acquire # {e.VisualAcquiredSerial,6}";
        }

        private void OnGUI()
        {
            if (!showDebug || !alsoDrawOnGUI || encounter == null)
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

            string s = Format(encounter);
            const float w = 240f;
            const float h = 340f;
            Rect rect = new Rect(Screen.width - w - 12f, onGuiTopOffset, w, h);

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f), s, _style);
        }
    }
}
