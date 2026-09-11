using UnityEngine;
using CreatureExperiment.Creature;

namespace CreatureExperiment.Creature.Perception
{
    /// <summary>
    /// Hearing 0.1 - the Creature's second sense, independent of <c>CreaturePerception</c>'s existing
    /// visual/range perception. Subscribes to a <see cref="PlayerMovementSoundEmitter"/>'s
    /// <c>Emitted</c> event, judges each <see cref="SoundStimulus"/> by distance vs. a simple
    /// distance*intensity range model, and - if heard - briefly orients the gaze toward where the
    /// sound came from via <c>CreaturePerception.SetAuditoryOrientPoint</c>.
    ///
    /// <b>Hearing a sound is NOT identifying the Player.</b> This component never writes
    /// <c>CreaturePerception.IsPlayerPerceived</c> (there is no setter for it - visual/range
    /// perception is computed entirely by CreaturePerception itself, untouched here), never sets an
    /// attention/behaviour target, never reads <see cref="SoundStimulus.Source"/> to shortcut "this
    /// was the Player". What it knows is exactly "a stimulus of this loudness happened at this
    /// position, this recently" - nothing more. If the resulting orient happens to turn the gaze
    /// toward the Player and the Player is then within CreaturePerception's own FOV/LOS/range gate,
    /// CreaturePerception's UNCHANGED algorithm picks the Player up on its own next frame - Hearing
    /// only ever causes the LOOK, never the recognition.
    ///
    /// Reaction is orient-only, and mild: <see cref="SetAuditoryOrientPoint"/> on <c>CreaturePerception</c>
    /// is a pure gaze-aim seam, the same shape as <c>SetForcePlayerGaze</c> - it never touches
    /// <c>AttendedInteractable</c>, <c>CreatureMovement</c>'s Approach/Inspect target, Wander,
    /// PlayerObserve, or any Probe state. A Creature deep in Object Inspect or mid-Probe keeps doing
    /// exactly that; a loud sound only makes its HEAD glance toward the noise for
    /// <see cref="auditoryOrientDuration"/> seconds before returning to whatever the gaze priority
    /// chain would otherwise show (see <c>CreaturePerception.Update</c>).
    ///
    /// Detection model, deliberately simple: <c>effectiveRange = maxHearingRange * stimulus.Intensity</c>
    /// - flat XZ distance, no inverse-square falloff, no wall/LOS occlusion (a wall does not block
    /// sound at all in 0.1 - see Report). This makes the Crouch/Normal/Dash/Jump intensity tiers
    /// directly visible as a detection-RANGE difference: the same footstep at the same distance may go
    /// unheard while crouched and heard while dashing.
    ///
    /// No repeat-stimulus jitter: while already orienting, a new stimulus only REFRESHES the orient
    /// (resets its duration and moves the point) if it is meaningfully louder
    /// (<see cref="refreshIntensityMargin"/>) than the one currently driving it - a train of ordinary
    /// footstep stimuli at the same loudness does not restart/twitch the gaze on every interval; it
    /// just lets the current orient run to completion.
    /// </summary>
    [RequireComponent(typeof(CreaturePerception))]
    public class CreatureHearing : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The sound emitter to listen to. Empty = auto-found in the scene (the Player's).")]
        [SerializeField] private PlayerMovementSoundEmitter emitter;
        [Tooltip("This Creature's CreaturePerception - only SetAuditoryOrientPoint is ever called on it; its perception algorithm is untouched. Empty = this GameObject's own CreaturePerception.")]
        [SerializeField] private CreaturePerception perception;

        [Header("Hearing (simple distance * intensity model - no occlusion, see class summary)")]
        [Tooltip("Hearing distance, metres, at intensity 1.0 (a Jump). effectiveRange = maxHearingRange * stimulus.Intensity.")]
        [SerializeField] private float maxHearingRange = 16f;

        [Header("Orient (gaze only)")]
        [Tooltip("Seconds a heard stimulus keeps the gaze oriented toward it before releasing back to the normal gaze priority.")]
        [SerializeField] private float auditoryOrientDuration = 0.7f;
        [Tooltip("While already orienting, a new heard stimulus only refreshes the orient (resets the duration, moves the point) if its intensity is at least this much higher than the one currently driving it - stops a train of same-loudness footsteps from restarting/twitching the gaze every interval.")]
        [SerializeField] private float refreshIntensityMargin = 0.1f;
        [Tooltip("How long a heard stimulus counts as 'recent' for HasRecentHeardStimulus, seconds. Debug/inspection convenience only.")]
        [SerializeField] private float recentWindowSeconds = 1.5f;

        private float _orientEndTime = -1f;
        private Vector3 _orientPoint;
        private bool _hasHeard;
        private float _lastHeardTime;

        /// <summary>World position of the most recent stimulus this Creature actually heard (within range). Meaningless before the first heard stimulus.</summary>
        public Vector3 LastHeardPosition { get; private set; }

        /// <summary>Intensity (0..1) of the most recent heard stimulus.</summary>
        public float LastHeardIntensity { get; private set; }

        /// <summary>Type of the most recent heard stimulus.</summary>
        public SoundStimulusType LastHeardType { get; private set; }

        /// <summary>Seconds since the last heard stimulus, or <see cref="float.PositiveInfinity"/> if none has ever been heard.</summary>
        public float TimeSinceLastHeard => _hasHeard ? Time.time - _lastHeardTime : float.PositiveInfinity;

        /// <summary>Monotonically increasing count of every stimulus actually heard (in range) - never decreases. Compare a snapshot to detect a NEW heard edge, same idiom as PlayerObservation's High/LowViewEpisodeSerial.</summary>
        public int HeardStimulusSerial { get; private set; }

        /// <summary>True if a stimulus was heard within the last <see cref="recentWindowSeconds"/> seconds. Debug/inspection convenience only.</summary>
        public bool HasRecentHeardStimulus => _hasHeard && TimeSinceLastHeard <= recentWindowSeconds;

        /// <summary>True while an auditory orient is currently driving the gaze (see CreaturePerception.SetAuditoryOrientPoint).</summary>
        public bool IsOrienting => Time.time < _orientEndTime;

        private void Awake()
        {
            if (perception == null)
                perception = GetComponent<CreaturePerception>();
            if (emitter == null)
                emitter = FindFirstObjectByType<PlayerMovementSoundEmitter>();
        }

        private void OnEnable()
        {
            if (emitter != null)
                emitter.Emitted += OnStimulusEmitted;
        }

        private void OnDisable()
        {
            if (emitter != null)
                emitter.Emitted -= OnStimulusEmitted;
            if (perception != null)
                perception.SetAuditoryOrientPoint(null); // never leave the gaze pinned by a component that just disabled
            _orientEndTime = -1f;
        }

        // The one detection judgement: distance vs. a loudness-scaled range. Nothing about WHO made
        // the sound is used here or anywhere below - see class summary.
        private void OnStimulusEmitted(SoundStimulus stimulus)
        {
            Vector3 flat = stimulus.Position - transform.position;
            flat.y = 0f;
            float distance = flat.magnitude;
            float effectiveRange = maxHearingRange * stimulus.Intensity;
            if (distance > effectiveRange)
                return; // not heard

            bool refresh = !IsOrienting || stimulus.Intensity >= LastHeardIntensity + refreshIntensityMargin;

            LastHeardPosition = stimulus.Position;
            LastHeardIntensity = stimulus.Intensity;
            LastHeardType = stimulus.Type;
            _hasHeard = true;
            _lastHeardTime = Time.time;
            HeardStimulusSerial++;

            if (refresh)
            {
                _orientPoint = stimulus.Position;
                _orientEndTime = Time.time + auditoryOrientDuration;
            }
        }

        // Pure actuator tick: while an orient is active, keep telling CreaturePerception where to look;
        // the instant it expires, stop. All the interesting timing/refresh logic already happened in
        // OnStimulusEmitted - this just reflects IsOrienting/_orientPoint onto the gaze seam every frame.
        private void Update()
        {
            if (perception == null)
                return;

            perception.SetAuditoryOrientPoint(IsOrienting ? (Vector3?)_orientPoint : null);
        }

        private void OnValidate()
        {
            maxHearingRange = Mathf.Max(0.1f, maxHearingRange);
            auditoryOrientDuration = Mathf.Max(0.05f, auditoryOrientDuration);
            refreshIntensityMargin = Mathf.Max(0f, refreshIntensityMargin);
            recentWindowSeconds = Mathf.Max(0f, recentWindowSeconds);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.5f);
            DrawFlatCircle(transform.position, maxHearingRange); // range at intensity 1.0 (Jump)

            if (Application.isPlaying && _hasHeard)
            {
                Gizmos.color = IsOrienting ? new Color(0.3f, 1f, 0.4f) : new Color(0.3f, 0.6f, 1f, 0.6f);
                Gizmos.DrawWireSphere(LastHeardPosition, 0.25f);
                Gizmos.DrawLine(transform.position, LastHeardPosition);
            }
        }

        private static void DrawFlatCircle(Vector3 centre, float radius)
        {
            const int segments = 40;
            Vector3 prev = centre + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 next = centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
