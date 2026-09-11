using UnityEngine;
using TMPro;

namespace CreatureExperiment.Creature.Brain
{
    /// <summary>
    /// Debug-only readout of <see cref="ReactionEpisodeRecorder"/>. Its own HUD class, separate from
    /// <see cref="PlayerObservationHUD"/> and <see cref="CreatureEncounterObservationHUD"/> - sensor
    /// code never touches HUD code. Drawn top-LEFT (the other two debug panels already stack on the
    /// right) so all three can be on screen without overlapping <c>ObjectiveHUD</c> (top-centre) or
    /// each other. The point of this one is not any single sensor value - it is reading, at a glance,
    /// "in the CreatureNear event, how did the Player's behaviour differ before vs. after". Purely a
    /// readout - it does not touch <see cref="ReactionEpisodeRecorder"/> or any Creature system.
    /// </summary>
    public class ReactionEpisodeHUD : MonoBehaviour
    {
        [Tooltip("The recorder to display. Auto-found on this GameObject or its parent, else anywhere in the scene, if left empty.")]
        [SerializeField] private ReactionEpisodeRecorder recorder;
        [Tooltip("Optional. A TextMeshPro / TextMeshProUGUI text to mirror the readout into.")]
        [SerializeField] private TMP_Text text;
        [Tooltip("Show the HUD at all. Off = fully hidden (both the TMP text, if assigned, and OnGUI).")]
        [SerializeField] private bool showDebug = true;
        [Tooltip("Also draw the readout with OnGUI (top-left corner) so it works with no UI set up.")]
        [SerializeField] private bool alsoDrawOnGUI = true;
        [SerializeField] private int onGuiFontSize = 14;

        private GUIStyle _style;

        private void Awake()
        {
            if (recorder == null)
                recorder = GetComponentInParent<ReactionEpisodeRecorder>();
            if (recorder == null)
                recorder = FindFirstObjectByType<ReactionEpisodeRecorder>();
        }

        private void Update()
        {
            if (!showDebug || recorder == null)
            {
                if (text != null && text.gameObject.activeSelf)
                    text.text = "";
                return;
            }

            if (text != null)
                text.text = Format(recorder);
        }

        private static string Yn(bool b) => b ? "YES" : "NO";

        private static string EventLabel(ReactionEpisodeRecorder.ReactionEventType t) =>
            t == ReactionEpisodeRecorder.ReactionEventType.CreatureVisualAcquired ? "VISUAL ACQUIRED" : "CREATURE NEAR";

        private static string OriginLabel(ReactionEpisodeRecorder.ReactionEpisodeOrigin o) =>
            o switch
            {
                ReactionEpisodeRecorder.ReactionEpisodeOrigin.PlayerDiscovery => "PLAYER DISCOVERY",
                ReactionEpisodeRecorder.ReactionEpisodeOrigin.SpatialEncounter => "SPATIAL ENCOUNTER",
                _ => "CREATURE INITIATED",
            };

        private static string Format(ReactionEpisodeRecorder r)
        {
            string state = r.State.ToString().ToUpperInvariant();

            string gazeLine = !r.HasGazeLatency
                ? "Gaze         NO\n"
                : $"Gaze        {Yn(r.GazeStartedAfterEvent),4}\n" +
                  $"Latency   {r.GazeLatency,6:F2} s\n";

            string secondary =
                (r.SecondaryVisualAcquiredOccurred ? $"2nd Visual@{r.SecondaryVisualAcquireTimeFromEvent,5:F1}s\n" : "") +
                (r.SecondaryNearOccurred ? $"2nd Near  @{r.SecondaryNearTimeFromEvent,5:F1}s\n" : "");

            string creatureObserved = !r.HasCreatureObservedData
                ? "CREATURE OBSERVED\nNONE\n"
                : "CREATURE OBSERVED\n" +
                  $"Looking   {r.ObservedLookingDuration,6:F2} s\n" +
                  $"Approach  {r.ObservedPeakApproachSpeed,6:F2} m/s\n" +
                  $"Retreat   {r.ObservedPeakRetreatSpeed,6:F2} m/s\n" +
                  $"Move Min  {r.ObservedMinMoveSpeed,6:F2} m/s\n" +
                  $"Move Max  {r.ObservedMaxMoveSpeed,6:F2} m/s\n";

            return
                "REACTION EPISODE\n\n" +
                $"State     {state,10}\n" +
                $"Origin    {OriginLabel(r.Origin),10}\n" +
                $"Event     {EventLabel(r.EventType),10}\n" +
                $"Distance  {r.EventDistance,6:F2} m\n" +
                secondary + "\n" +

                $"BEFORE {r.BeforeWindowSeconds,3:F1}s\n" +
                $"Move Avg  {r.BeforeAverageMoveSpeed,6:F2} m/s\n" +
                $"View Dev  {r.BeforeAverageViewDeviation,6:F2}x\n" +
                $"Toward Avg{r.BeforeAverageTowardSpeed,6:F2} m/s\n" +
                $"Looking % {r.BeforeWasLookingRatio * 100f,6:F0} %\n\n" +

                "WORLD REACTION\n" +
                $"AFTER {r.AfterWindowSeconds,4:F1}s\n" +
                $"Move Avg  {r.AverageMoveSpeedAfter,6:F2} m/s\n" +
                $"Move Min  {r.MinMoveSpeedAfter,6:F2} m/s\n" +
                $"Move Max  {r.MaxMoveSpeedAfter,6:F2} m/s\n" +
                $"View Peak {r.PeakViewDeviation,6:F2}x\n" +
                $"High      {Yn(r.HighViewOccurred),6}\n" +
                $"Low       {Yn(r.LowViewOccurred),6}\n" +
                gazeLine +
                $"Gaze Time {r.AfterTotalGazeTime,6:F2} s\n" +
                $"Longest   {r.AfterLongestGaze,6:F2} s\n" +
                $"Approach  {r.PeakApproachSpeed,6:F2} m/s\n" +
                $"Retreat   {r.PeakRetreatSpeed,6:F2} m/s\n\n" +

                "CREATURE PERCEPTION\n" +
                $"Aware@Event{Yn(r.CreatureAwareAtEvent),5}\n" +
                $"BecameAware{Yn(r.CreatureBecameAwareDuringEpisode),5}\n" +
                $"Awareness {(r.HasCreatureAwarenessLatency ? $"{r.CreatureAwarenessLatency,5:F2}s" : "  n/a")}\n" +
                $"ObservTime{r.CreatureObservableDuration,6:F2}s\n\n" +

                creatureObserved;
        }

        private void OnGUI()
        {
            if (!showDebug || !alsoDrawOnGUI || recorder == null)
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

            string s = Format(recorder);
            const float w = 260f;
            const float h = 620f;
            Rect rect = new Rect(12f, 12f, w, h);

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f), s, _style);
        }
    }
}
