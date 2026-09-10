using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// TEMPORARY diagnostic - drop this on the creature to trace why an Object Attention Budget
    /// episode never expires while it chases a rolling Sphere. Renders an OnGUI HUD and logs a line
    /// to the Console whenever a tracked value changes (plus a 0.5 s heartbeat). Reads only public /
    /// Debug* accessors; changes nothing. Delete this file + the Debug* getters on CreaturePerception
    /// when done.
    /// </summary>
    [DisallowMultipleComponent]
    public class CreatureAttentionDebug : MonoBehaviour
    {
        [Tooltip("Also print a line to the Console on every change / 0.5s heartbeat.")]
        [SerializeField] private bool logToConsole = true;
        [Tooltip("Top-left screen offset of the HUD, pixels.")]
        [SerializeField] private Vector2 hudOrigin = new Vector2(12f, 12f);

        private CreaturePerception _perception;
        private CreatureMovement _movement;
        private CreatureWander _wander;
        private CreaturePlayerObserve _observe;
        private CreatureProbe _probe;
        private CreatureThrowProbe _throwProbe;

        private string _hud = "";
        private string _lastSig = "";
        private float _heartbeat;

        private void Awake()
        {
            _perception = GetComponent<CreaturePerception>();
            _movement = GetComponent<CreatureMovement>();
            _wander = GetComponent<CreatureWander>();
            _observe = GetComponent<CreaturePlayerObserve>();
            _probe = GetComponent<CreatureProbe>();
            _throwProbe = GetComponent<CreatureThrowProbe>();
        }

        private static string N(Object o) => o == null ? "-" : o.name;

        private void LateUpdate()
        {
            // The "focus object" the trace is about: whichever of these the creature is engaged with.
            Interactable attended = _perception != null ? _perception.AttendedInteractable : null;
            Interactable episode = _perception != null ? _perception.DebugEpisodeObject : null;
            Interactable cooldown = _perception != null ? _perception.AttentionCooldownObject : null;
            Interactable active = _movement != null ? _movement.InspectTarget : null;
            Interactable lost = _movement != null ? _movement.LostTargetInteractable : null;

            Interactable focus = attended ?? active ?? lost ?? episode ?? cooldown;
            bool inFlight = focus != null && focus.IsInFlight;

            float elapsed = _perception != null ? _perception.DebugEpisodeElapsed : 0f;
            float budget = _perception != null ? _perception.DebugObjectAttentionBudget : 0f;
            float cdTimer = _perception != null ? _perception.DebugAttentionCooldownTimer : 0f;
            bool forceGaze = _perception != null && _perception.DebugForcePlayerGaze;

            bool approaching = _movement != null && _movement.IsApproaching;
            bool inspecting = _movement != null && _movement.IsInspecting;
            bool drivingRoot = _movement != null && _movement.IsDrivingRoot;

            bool wanderActive = _wander != null && _wander.State != CreatureWander.WanderState.Idle;
            bool observeEngaged = _observe != null && _observe.IsEngaged;
            string probeState = _probe != null ? _probe.State.ToString() : "-";
            string throwProbeState = _throwProbe != null ? _throwProbe.State.ToString() : "-";
            // "Probe is driving root" == CreatureProbe.Delivering (the only phase that calls
            // SetProbeApproachTarget). ThrowProbe never drives root.
            bool probeDrivingRoot = _probe != null && _probe.State == CreatureProbe.ProbeState.Delivering;

            _hud =
                $"focus            : {N(focus)}   IsInFlight={inFlight}\n" +
                $"AttendedInteractable : {N(attended)}\n" +
                $"EpisodeObject    : {N(episode)}\n" +
                $"EpisodeElapsed   : {elapsed:F2} / {budget:F1}\n" +
                $"AttentionCooldown: {N(cooldown)}  (t={cdTimer:F2})\n" +
                $"forcePlayerGaze  : {forceGaze}\n" +
                $"----\n" +
                $"Move._activeTarget(InspectTarget): {N(active)}\n" +
                $"Move.IsApproaching : {approaching}\n" +
                $"Move.IsInspecting  : {inspecting}\n" +
                $"Move.LostTargetInteractable : {N(lost)}\n" +
                $"Move.IsDrivingRoot : {drivingRoot}\n" +
                $"----\n" +
                $"Wander active     : {wanderActive}\n" +
                $"PlayerObserve.IsEngaged : {observeEngaged}\n" +
                $"Probe.State       : {probeState}   (drivingRoot={probeDrivingRoot})\n" +
                $"ThrowProbe.State  : {throwProbeState}";

            if (!logToConsole)
                return;

            // Log on any change to a compact signature, plus a 0.5s heartbeat.
            string sig = $"{N(attended)}|{N(episode)}|{elapsed:F1}|{N(cooldown)}|{inFlight}|" +
                         $"{N(active)}|{approaching}|{inspecting}|{N(lost)}|{drivingRoot}|" +
                         $"{wanderActive}|{observeEngaged}|{probeState}|{forceGaze}";
            _heartbeat += Time.deltaTime;
            if (sig != _lastSig || _heartbeat >= 0.5f)
            {
                _lastSig = sig;
                _heartbeat = 0f;
                Debug.Log(
                    $"[AttnDebug] f{Time.frameCount} t{Time.time:F2} " +
                    $"att={N(attended)} epi={N(episode)} elapsed={elapsed:F2}/{budget:F1} " +
                    $"cd={N(cooldown)}({cdTimer:F2}) inFlight={inFlight} " +
                    $"activeTgt={N(active)} appr={approaching} insp={inspecting} " +
                    $"lostTgt={N(lost)} drivingRoot={drivingRoot} " +
                    $"wander={wanderActive} observe={observeEngaged} probe={probeState} " +
                    $"throwProbe={throwProbeState} forceGaze={forceGaze}");
            }
        }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                richText = false,
            };
            style.normal.textColor = Color.white;

            var rect = new Rect(hudOrigin.x, hudOrigin.y, 460f, 360f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f), _hud, style);
        }
    }
}
