using System.Collections.Generic;
using UnityEngine;

namespace CreatureExperiment.Creature.Brain
{
    /// <summary>
    /// Player behaviour sensor 0.2 - the first piece of infrastructure for a future Creature "Brain"
    /// that watches how a Player tends to play, not just where things are. This component measures
    /// gameplay-RESULT Player behaviour only: real world-space movement and real view (camera)
    /// rotation. It does not read raw input (key state, mouse delta) - two Players with different
    /// mouse sensitivity or key-repeat should still produce comparable numbers, because both are
    /// measured after the game already turned their input into a position and an orientation.
    ///
    /// Deliberately standalone: no reference to anything under <c>CreatureExperiment.Creature</c>
    /// (this file lives in the <c>Brain</c> folder only because that is its eventual consumer), no
    /// reference to <c>CreaturePerception</c>/<c>CreatureMovement</c>/Attention/Probe/
    /// <c>DailyLifeDirector</c>. It runs identically with zero Creatures in the scene. Nothing reads
    /// these numbers yet - no Brain, no gate, no emotion/personality inference, no Creature reaction.
    /// Pure measurement.
    ///
    /// Movement (0.1, unchanged): <see cref="CurrentMoveSpeed"/> is the flat (XZ) world-space distance
    /// <see cref="playerRoot"/> actually travelled this frame, divided by that frame's delta time -
    /// not "is a move key held". <see cref="StillRatio"/> is the fraction of <see cref="baselineWindowSeconds"/>
    /// spent at or below <see cref="stillSpeedThreshold"/>.
    ///
    /// View (0.2 - relative, not absolute): <see cref="CurrentViewAngularSpeed"/> is
    /// <see cref="Quaternion.Angle"/> between this frame's and last frame's <see cref="viewTransform"/>
    /// rotation, divided by delta time - the actual camera orientation (yaw AND pitch together),
    /// never a raw Euler subtraction (which would misread a 359 deg -&gt; 0 deg wrap as a 359 deg spin)
    /// and never the raw mouse delta (sensitivity-dependent). 0.1 flagged a "burst" on one absolute
    /// threshold, which over-fired for a Player whose normal play style is already fast (e.g. quick
    /// mouse flicks while Sorting) - 20-30 bursts in a short span. 0.2 instead compares two
    /// time-weighted averages of the SAME signal at two time scales: <see cref="BaselineViewAngularSpeed"/>
    /// (this Player's recent normal activity, over <see cref="baselineWindowSeconds"/>, default 10s)
    /// against <see cref="ShortViewAngularSpeed"/> (right-now activity, over <see cref="shortViewWindowSeconds"/>,
    /// default 0.35s - long enough to be a real reaction, not a single-frame spike). A High/Low View
    /// Episode is about <see cref="ShortViewAngularSpeed"/> vs <see cref="BaselineViewAngularSpeed"/>,
    /// never the raw per-frame <see cref="CurrentViewAngularSpeed"/>. A sudden big movement still
    /// nudges the baseline up a little (it is one more sample in that window) - this version does not
    /// try to exclude it; that is future work (long-term / event-excluded baseline), not this step.
    ///
    /// This is measurement, not interpretation: it can say "right now is a lot faster/slower than
    /// this Player's recent normal", nothing about surprise, fear, or any other meaning.
    ///
    /// Rolling window: every average / ratio / episode count here is over the last N seconds only -
    /// no lifetime statistic. All of it is time-weighted (each frame contributes its own delta time
    /// as its sample's weight) and evicted by wall-clock time, independent of frame rate.
    ///
    /// Everything here is read-only from outside (auto-properties with a private setter) - nothing
    /// external can reach into the sample queues.
    /// </summary>
    public class PlayerObservation : MonoBehaviour
    {
        private enum ViewEpisodeState { Normal, High, LowCandidate, Low }

        [Header("References")]
        [Tooltip("Player root whose world-space XZ movement is measured. Empty = this GameObject's own transform.")]
        [SerializeField] private Transform playerRoot;
        [Tooltip("Transform whose rotation IS the player's view direction (yaw + pitch combined) - the actual camera, not the yaw-only body or a raw look-input consumer. Empty = Camera.main, else the first Camera found in children.")]
        [SerializeField] private Transform viewTransform;

        [Header("Windows")]
        [Tooltip("Seconds of history behind: Movement's Average/StillRatio, View's Baseline average, and the High/Low View episode counts (an episode drops out of the count once its timestamp ages past this). This Player's 'recent normal'.")]
        [SerializeField] private float baselineWindowSeconds = 10f;
        [Tooltip("Seconds of history behind ShortViewAngularSpeed - long enough to be a real short burst of activity, not a single frame's spike. This Player's 'right now'.")]
        [SerializeField] private float shortViewWindowSeconds = 0.35f;
        [Tooltip("Seconds of baseline history that must have accumulated before the High/Low View episode detector is trusted. Raw Movement/View numbers (including CurrentViewAngularSpeed, BaselineViewAngularSpeed, ShortViewAngularSpeed) are shown immediately; only High/Low detection waits.")]
        [SerializeField] private float baselineWarmupSeconds = 3f;

        [Header("Movement")]
        [Tooltip("Flat (XZ) move speed, m/s, at or below which the Player counts as stationary for StillRatio. PlayerMovement's crouch speed is 2 m/s, walk 4, sprint 7 - this stays well under all three so only genuine standing-still counts, tolerating small CharacterController jitter.")]
        [SerializeField] private float stillSpeedThreshold = 0.1f;
        [Tooltip("Simple teleport/scene-reset guard: an instantaneous move speed above this (m/s) is treated as a non-gameplay jump (e.g. Bed's day-rollover teleport), not real movement - that one frame's position delta is discarded instead of being recorded as a huge spike.")]
        [SerializeField] private float teleportSpeedGuard = 20f;

        [Header("View deviation ratio")]
        [Tooltip("Divisor floor for ViewDeviationRatio = ShortViewAngularSpeed / max(BaselineViewAngularSpeed, this). Stops a near-zero baseline (Player has been essentially motionless) from blowing the ratio up to a meaningless huge number.")]
        [SerializeField] private float minimumBaselineForRatio = 10f;

        [Header("High View Episode")]
        [Tooltip("Absolute floor: ShortViewAngularSpeed must be at least this fast (deg/s) to ever start a High View Episode, no matter how low the baseline is.")]
        [SerializeField] private float minimumHighViewSpeed = 180f;
        [Tooltip("Relative trigger: a High View Episode starts once ShortViewAngularSpeed reaches BaselineViewAngularSpeed times this (subject to the absolute floor above too - the effective start threshold is the larger of the two).")]
        [SerializeField] private float highViewMultiplier = 2.5f;
        [Tooltip("Absolute floor for ending a High View Episode (deg/s).")]
        [SerializeField] private float minimumHighViewReleaseSpeed = 100f;
        [Tooltip("Relative release: an active High View Episode ends once ShortViewAngularSpeed drops to BaselineViewAngularSpeed times this (the effective release threshold is the larger of this and the absolute floor above) - kept below highViewMultiplier so one whip is not counted as several episodes as it decelerates through the start threshold.")]
        [SerializeField] private float highViewReleaseMultiplier = 1.5f;

        [Header("Low View Episode")]
        [Tooltip("A Low View Episode can only be considered while the Player's OWN baseline is at least this active (deg/s). Without this floor, a Player who is simply calm/still from the start would be flagged as constantly 'Low', which is wrong - Low means a drop FROM this Player's own normal activity.")]
        [SerializeField] private float minimumBaselineForLowView = 40f;
        [Tooltip("Relative trigger: the Low View condition holds while ShortViewAngularSpeed is at or below BaselineViewAngularSpeed times this.")]
        [SerializeField] private float lowViewRatio = 0.25f;
        [Tooltip("Seconds the Low View condition must hold continuously before it is confirmed as a Low View Episode (and counted). A 1-2 frame dip does not count - this is Low View's own hysteresis-in-time, separate from the release ratio below.")]
        [SerializeField] private float lowViewMinDuration = 0.4f;
        [Tooltip("Relative release: an active Low View Episode ends once ShortViewAngularSpeed recovers to BaselineViewAngularSpeed times this (kept above lowViewRatio so recovery does not flicker the episode on/off at the boundary).")]
        [SerializeField] private float lowViewReleaseRatio = 0.5f;

        // --- rolling sample window (movement + baseline view) ---------------------------------------
        private struct Sample
        {
            public float time;   // Time.time this sample was taken
            public float dt;     // that frame's Time.deltaTime - this sample's time-weight
            public float moveSpeed;
            public float viewAngularSpeed;
            public bool still;
        }

        private readonly Queue<Sample> _samples = new Queue<Sample>();
        private float _sumDt;            // sum of dt over samples currently in the baseline window - also IS the warmup clock
        private float _sumMoveWeighted;  // sum of moveSpeed * dt
        private float _sumViewWeighted;  // sum of viewAngularSpeed * dt (baseline)
        private float _sumStillDt;       // sum of dt where still == true

        // --- short view window (right-now reaction) -------------------------------------------------
        private struct ViewSample
        {
            public float time;
            public float dt;
            public float viewAngularSpeed;
        }

        private readonly Queue<ViewSample> _shortViewSamples = new Queue<ViewSample>();
        private float _shortSumDt;
        private float _shortSumViewWeighted;

        // --- completed High / Low View episodes within the baseline window --------------------------
        private struct HighEpisode { public float time; public float peak; }
        private readonly Queue<HighEpisode> _highEpisodes = new Queue<HighEpisode>();
        private float _sumHighPeak;

        private readonly Queue<float> _lowEpisodes = new Queue<float>(); // just the end timestamps

        // --- View episode state machine (Normal / High / LowCandidate / Low - mutually exclusive by
        //     construction: exactly one of these four at a time) ------------------------------------
        private ViewEpisodeState _viewState = ViewEpisodeState.Normal;
        private float _highPeakInProgress;     // running peak while _viewState == High
        private float _lowConditionTimer;      // seconds the Low condition has held, from LowCandidate through Low

        private Vector3 _prevPos;
        private Quaternion _prevViewRot;
        private bool _initialized;

        /// <summary>Flat (XZ) world-space speed of <see cref="playerRoot"/> this frame, m/s. 0 on a teleport-guarded frame.</summary>
        public float CurrentMoveSpeed { get; private set; }

        /// <summary>Time-weighted average of <see cref="CurrentMoveSpeed"/> over the last <see cref="baselineWindowSeconds"/> seconds.</summary>
        public float AverageMoveSpeed { get; private set; }

        /// <summary>Fraction (0..1) of the last <see cref="baselineWindowSeconds"/> seconds spent at or below <see cref="stillSpeedThreshold"/>.</summary>
        public float StillRatio { get; private set; }

        /// <summary>Angular speed of <see cref="viewTransform"/>'s rotation this frame, deg/s (yaw + pitch combined, via <see cref="Quaternion.Angle"/>). Raw, single-frame - never compared directly against the baseline; see <see cref="ShortViewAngularSpeed"/>.</summary>
        public float CurrentViewAngularSpeed { get; private set; }

        /// <summary>This Player's recent-normal View activity: time-weighted average of <see cref="CurrentViewAngularSpeed"/> over the last <see cref="baselineWindowSeconds"/> seconds.</summary>
        public float BaselineViewAngularSpeed { get; private set; }

        /// <summary>This Player's right-now View activity: time-weighted average of <see cref="CurrentViewAngularSpeed"/> over the last <see cref="shortViewWindowSeconds"/> seconds (a short real span, not a single frame).</summary>
        public float ShortViewAngularSpeed { get; private set; }

        /// <summary>
        /// <see cref="ShortViewAngularSpeed"/> / max(<see cref="BaselineViewAngularSpeed"/>, <see cref="minimumBaselineForRatio"/>).
        /// 1.0x = right now matches this Player's own recent normal; &gt;1x = faster than normal; &lt;1x = slower.
        /// Always computed (not warmup-gated) - only the High/Low episode DETECTOR waits for warmup.
        /// </summary>
        public float ViewDeviationRatio { get; private set; }

        /// <summary>True once enough baseline history has accumulated (<see cref="baselineWarmupSeconds"/>) for the High/Low View episode detector to be trusted.</summary>
        public bool IsBaselineWarmedUp => _sumDt >= baselineWarmupSeconds;

        /// <summary>True while a High View Episode is active right now.</summary>
        public bool IsHighViewActive => _viewState == ViewEpisodeState.High;

        /// <summary>True while a Low View condition is being watched but not yet confirmed (has not held for <see cref="lowViewMinDuration"/> yet). Debug/inspection only - not counted as an episode.</summary>
        public bool IsLowViewCandidate => _viewState == ViewEpisodeState.LowCandidate;

        /// <summary>True while a confirmed Low View Episode is active right now.</summary>
        public bool IsLowViewActive => _viewState == ViewEpisodeState.Low;

        /// <summary>Seconds the current Low View condition has held continuously (candidate + confirmed together), or 0 when not in a Low candidate/active state. Debug aid only.</summary>
        public float CurrentLowViewDuration =>
            (_viewState == ViewEpisodeState.LowCandidate || _viewState == ViewEpisodeState.Low) ? _lowConditionTimer : 0f;

        /// <summary>How many High View Episodes COMPLETED within the last <see cref="baselineWindowSeconds"/> seconds. A still-active episode is not counted yet.</summary>
        public int HighViewEpisodeCount { get; private set; }

        /// <summary>Average peak <see cref="ShortViewAngularSpeed"/> (deg/s) of the completed High View Episodes counted in <see cref="HighViewEpisodeCount"/>. 0 when there are none.</summary>
        public float AverageHighViewPeak { get; private set; }

        /// <summary>How many Low View Episodes were CONFIRMED (held <see cref="lowViewMinDuration"/>+) within the last <see cref="baselineWindowSeconds"/> seconds.</summary>
        public int LowViewEpisodeCount { get; private set; }

        /// <summary>
        /// Monotonically increasing count of every High View Episode ever confirmed by this component
        /// (never decreases, never windowed/evicted - unlike <see cref="HighViewEpisodeCount"/>). Exists
        /// so a consumer (e.g. a Reaction Episode recorder) can snapshot this value and later ask "did a
        /// NEW High View Episode happen since then?" via a simple greater-than check, without the
        /// rolling <see cref="HighViewEpisodeCount"/>'s ambiguity (it can rise AND fall from unrelated
        /// evictions within the same short window). Sensor responsibility only - this is still just a
        /// count, no interpretation.
        /// </summary>
        public int HighViewEpisodeSerial { get; private set; }

        /// <summary>Same as <see cref="HighViewEpisodeSerial"/>, for Low View Episodes.</summary>
        public int LowViewEpisodeSerial { get; private set; }

        private void Awake()
        {
            if (playerRoot == null)
                playerRoot = transform;

            if (viewTransform == null)
            {
                var cam = Camera.main;
                if (cam == null)
                    cam = GetComponentInChildren<Camera>();
                if (cam != null)
                    viewTransform = cam.transform;
            }
        }

        // LateUpdate, not Update: PlayerMovement / PlayerLook are both Update() and Unity guarantees
        // every Update() this frame finishes before any LateUpdate() runs, so this always reads the
        // position/orientation AFTER this frame's input has actually been applied - no dependence on
        // component order (the same ambiguity that caused the Creature Object Attention Budget's
        // execution-order bug is sidestepped here rather than repeated).
        private void LateUpdate()
        {
            if (playerRoot == null)
                return;

            float dt = Time.deltaTime;
            float now = Time.time;

            if (!_initialized)
            {
                // First frame: seed instead of measuring a bogus jump from a default Transform state.
                _prevPos = playerRoot.position;
                _prevViewRot = viewTransform != null ? viewTransform.rotation : Quaternion.identity;
                _initialized = true;
                CurrentMoveSpeed = 0f;
                CurrentViewAngularSpeed = 0f;
                RecomputeWindows(now);
                return;
            }

            // --- movement -----------------------------------------------------------------------
            Vector3 pos = playerRoot.position;
            Vector3 flatDelta = pos - _prevPos;
            flatDelta.y = 0f;
            float rawMoveSpeed = dt > 0f ? flatDelta.magnitude / dt : 0f;
            _prevPos = pos;

            bool teleported = rawMoveSpeed > teleportSpeedGuard;
            CurrentMoveSpeed = teleported ? 0f : rawMoveSpeed;

            // --- view ---------------------------------------------------------------------------
            float rawViewAngularSpeed = 0f;
            if (viewTransform != null)
            {
                Quaternion rot = viewTransform.rotation;
                rawViewAngularSpeed = dt > 0f ? Quaternion.Angle(_prevViewRot, rot) / dt : 0f;
                _prevViewRot = rot;
            }
            CurrentViewAngularSpeed = rawViewAngularSpeed;

            if (dt > 0f)
            {
                _samples.Enqueue(new Sample
                {
                    time = now,
                    dt = dt,
                    moveSpeed = CurrentMoveSpeed,
                    viewAngularSpeed = CurrentViewAngularSpeed,
                    still = CurrentMoveSpeed <= stillSpeedThreshold,
                });
                _sumDt += dt;
                _sumMoveWeighted += CurrentMoveSpeed * dt;
                _sumViewWeighted += CurrentViewAngularSpeed * dt;
                if (CurrentMoveSpeed <= stillSpeedThreshold)
                    _sumStillDt += dt;

                _shortViewSamples.Enqueue(new ViewSample { time = now, dt = dt, viewAngularSpeed = CurrentViewAngularSpeed });
                _shortSumDt += dt;
                _shortSumViewWeighted += CurrentViewAngularSpeed * dt;
            }

            RecomputeWindows(now);
            UpdateViewEpisode(dt, now);
        }

        // Evict anything that has aged out of each window, then publish the cached public values.
        // Called once per LateUpdate so every property read this frame is consistent and cheap.
        private void RecomputeWindows(float now)
        {
            while (_samples.Count > 0 && now - _samples.Peek().time > baselineWindowSeconds)
            {
                Sample old = _samples.Dequeue();
                _sumDt -= old.dt;
                _sumMoveWeighted -= old.moveSpeed * old.dt;
                _sumViewWeighted -= old.viewAngularSpeed * old.dt;
                if (old.still)
                    _sumStillDt -= old.dt;
            }

            while (_shortViewSamples.Count > 0 && now - _shortViewSamples.Peek().time > shortViewWindowSeconds)
            {
                ViewSample old = _shortViewSamples.Dequeue();
                _shortSumDt -= old.dt;
                _shortSumViewWeighted -= old.viewAngularSpeed * old.dt;
            }

            while (_highEpisodes.Count > 0 && now - _highEpisodes.Peek().time > baselineWindowSeconds)
            {
                HighEpisode old = _highEpisodes.Dequeue();
                _sumHighPeak -= old.peak;
            }

            while (_lowEpisodes.Count > 0 && now - _lowEpisodes.Peek() > baselineWindowSeconds)
                _lowEpisodes.Dequeue();

            AverageMoveSpeed = _sumDt > 0f ? _sumMoveWeighted / _sumDt : 0f;
            StillRatio = _sumDt > 0f ? Mathf.Clamp01(_sumStillDt / _sumDt) : 0f;
            BaselineViewAngularSpeed = _sumDt > 0f ? _sumViewWeighted / _sumDt : 0f;
            ShortViewAngularSpeed = _shortSumDt > 0f ? _shortSumViewWeighted / _shortSumDt : 0f;
            ViewDeviationRatio = ShortViewAngularSpeed / Mathf.Max(BaselineViewAngularSpeed, minimumBaselineForRatio);

            HighViewEpisodeCount = _highEpisodes.Count;
            AverageHighViewPeak = _highEpisodes.Count > 0 ? _sumHighPeak / _highEpisodes.Count : 0f;
            LowViewEpisodeCount = _lowEpisodes.Count;
        }

        // Relative High/Low View Episode detector. ShortViewAngularSpeed vs BaselineViewAngularSpeed
        // ONLY - never the raw per-frame CurrentViewAngularSpeed. Disabled (forced Normal) until the
        // baseline has warmed up, so an empty-history baseline near start-of-scene cannot misfire.
        private void UpdateViewEpisode(float dt, float now)
        {
            if (!IsBaselineWarmedUp)
            {
                _viewState = ViewEpisodeState.Normal;
                _highPeakInProgress = 0f;
                _lowConditionTimer = 0f;
                return;
            }

            float highStart = Mathf.Max(minimumHighViewSpeed, BaselineViewAngularSpeed * highViewMultiplier);
            float highRelease = Mathf.Max(minimumHighViewReleaseSpeed, BaselineViewAngularSpeed * highViewReleaseMultiplier);
            bool lowCondition = BaselineViewAngularSpeed >= minimumBaselineForLowView
                                 && ShortViewAngularSpeed <= BaselineViewAngularSpeed * lowViewRatio;
            // Low ends either on relative recovery, or if the Player's own baseline itself has since
            // dropped below the "has real activity" floor (nothing left to be Low relative to).
            bool lowRelease = ShortViewAngularSpeed >= BaselineViewAngularSpeed * lowViewReleaseRatio
                               || BaselineViewAngularSpeed < minimumBaselineForLowView;

            switch (_viewState)
            {
                case ViewEpisodeState.Normal:
                    if (ShortViewAngularSpeed >= highStart)
                    {
                        _viewState = ViewEpisodeState.High;
                        _highPeakInProgress = ShortViewAngularSpeed;
                    }
                    else if (lowCondition)
                    {
                        _viewState = ViewEpisodeState.LowCandidate;
                        _lowConditionTimer = 0f;
                    }
                    break;

                case ViewEpisodeState.High:
                    if (ShortViewAngularSpeed > _highPeakInProgress)
                        _highPeakInProgress = ShortViewAngularSpeed;
                    if (ShortViewAngularSpeed <= highRelease)
                    {
                        _highEpisodes.Enqueue(new HighEpisode { time = now, peak = _highPeakInProgress });
                        _sumHighPeak += _highPeakInProgress;
                        HighViewEpisodeSerial++;
                        _viewState = ViewEpisodeState.Normal;
                        _highPeakInProgress = 0f;
                    }
                    break;

                case ViewEpisodeState.LowCandidate:
                    if (!lowCondition)
                    {
                        // Cancelled - never confirmed, so never counted.
                        _viewState = ViewEpisodeState.Normal;
                        _lowConditionTimer = 0f;
                    }
                    else
                    {
                        _lowConditionTimer += dt;
                        if (_lowConditionTimer >= lowViewMinDuration)
                        {
                            _lowEpisodes.Enqueue(now);
                            LowViewEpisodeSerial++;
                            _viewState = ViewEpisodeState.Low;
                        }
                    }
                    break;

                case ViewEpisodeState.Low:
                    if (lowRelease)
                    {
                        _viewState = ViewEpisodeState.Normal;
                        _lowConditionTimer = 0f;
                    }
                    else
                    {
                        _lowConditionTimer += dt;
                    }
                    break;
            }
        }

        private void OnValidate()
        {
            baselineWindowSeconds = Mathf.Max(0.5f, baselineWindowSeconds);
            shortViewWindowSeconds = Mathf.Clamp(shortViewWindowSeconds, 0.05f, baselineWindowSeconds);
            baselineWarmupSeconds = Mathf.Clamp(baselineWarmupSeconds, 0f, baselineWindowSeconds);

            stillSpeedThreshold = Mathf.Max(0f, stillSpeedThreshold);
            teleportSpeedGuard = Mathf.Max(0.1f, teleportSpeedGuard);

            minimumBaselineForRatio = Mathf.Max(0.01f, minimumBaselineForRatio);

            minimumHighViewSpeed = Mathf.Max(0.01f, minimumHighViewSpeed);
            highViewMultiplier = Mathf.Max(1f, highViewMultiplier);
            minimumHighViewReleaseSpeed = Mathf.Clamp(minimumHighViewReleaseSpeed, 0f, minimumHighViewSpeed);
            highViewReleaseMultiplier = Mathf.Clamp(highViewReleaseMultiplier, 0f, highViewMultiplier);

            minimumBaselineForLowView = Mathf.Max(0f, minimumBaselineForLowView);
            lowViewRatio = Mathf.Clamp(lowViewRatio, 0f, 1f);
            lowViewMinDuration = Mathf.Max(0f, lowViewMinDuration);
            lowViewReleaseRatio = Mathf.Clamp(lowViewReleaseRatio, lowViewRatio, 1f);
        }
    }
}
