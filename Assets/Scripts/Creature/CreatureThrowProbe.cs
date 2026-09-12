using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Throw Probe: sibling experiment to <see cref="CreatureProbe"/>. Two independent triggers, both
    /// driving the same grab -> hold -> aimed-throw pipeline (Imitative is one-shot-per-session,
    /// HitResponse is cooldown-gated repeatable - see the 0.3 notes below):
    ///   - <see cref="ThrowProbeMode.Imitative"/>: <see cref="CreaturePattern.RepeatedThrowMissNearDetected"/>
    ///     latches (the Player kept THROWING an object that missed the creature but landed NEAR) -> after
    ///     a deliberation delay the creature grabs that same object and throws it loosely playerward.
    ///   - <see cref="ThrowProbeMode.HitResponse"/>: a Player-thrown object actually HIT the creature
    ///     (<see cref="CreatureObservation.ThrowHitSerial"/> ticks, the same edge <c>PlayerSalience</c>
    ///     reads) -> after a short deliberation delay the creature grabs the object that hit it and
    ///     throws it back closely aimed at the Player.
    ///
    /// Both are the SAME physical act - "the Player did this, so the creature tries a similar physical
    /// action back once" - only the trigger and the aim precision differ. NOT attack / retaliation /
    /// play / gift / anger / fear. No meaning label, no Decision / Hypothesis / Intent / Interest /
    /// Search system, no emotion state. HitResponse gets priority (it is a stronger, more immediate
    /// stimulus - see <c>PlayerSalience.throwHitBonus</c>) and a shorter deliberation delay, but is
    /// otherwise structurally identical to Imitative: <see cref="ComputeAimDirection"/> blends the
    /// creature's own flat facing (Player contribution = 0) toward the flat toPlayer direction (Player
    /// contribution = 1) by a per-mode weight, then scatters the result by a random yaw - low weight +
    /// wide scatter for Imitative (weak, unreliable player-directionality), high weight + narrow scatter
    /// for HitResponse (clearly player-centered). toPlayer is always the Player's CURRENT flat position -
    /// no ballistic predictor, no force difference between the two.
    ///
    /// 0.3 - HitResponse is now REPEATABLE, not a one-shot: a completed attempt starts a random
    /// <see cref="hitResponseCooldownMin"/>..<see cref="hitResponseCooldownMax"/> cooldown instead of
    /// permanently spending it (<see cref="Terminate"/>). A NEW ThrowHit edge during cooldown is ignored
    /// for the throw-back behaviour (PlayerSalience/CreatureObservation still record it exactly as
    /// before - only this component's reaction is gated). Imitative stays one-shot-per-session (see the
    /// class's own report for why - its trigger is itself a permanent CreaturePattern latch, unlike
    /// HitResponse's repeatable edge).
    ///
    /// Pending + expiry (0.3): a hit that arms HitResponse while the creature cannot yet commit (busy
    /// with something else) stays "pending" for up to <see cref="hitResponsePendingLifetime"/> seconds;
    /// if it still cannot commit by then, the pending reaction quietly expires (no throw-back, no
    /// meaning attached) rather than firing a stale reaction to a much-earlier hit. A newer hit arriving
    /// while one is already pending REFRESHES the same pending slot (snapshot-and-replace) rather than
    /// queuing a second one - at most one HitResponse is ever pending.
    ///
    /// Priority over lower-priority behaviour (0.3, see <see cref="Update"/>): a pending, ready
    /// (off-cooldown) HitResponse can (a) upgrade THIS component's own in-progress Imitative attempt in
    /// place (same carry, same object - just switches which aim recipe <see cref="TickHolding"/> uses,
    /// only while it has not yet released), and (b) redirect the sibling <see cref="CreatureProbe"/>'s
    /// (Place Probe's) already-probe-owned carry into an aimed HitResponse throw via
    /// <see cref="CreaturePickup.RequestProbeReleaseAsThrow"/>. Both are safe because
    /// <see cref="CreaturePickup"/> already refuses that call unless the carry was itself started by a
    /// probe (its own <c>_probeCarry</c> guard) - so the creature's OWN autonomous (non-probe) carry can
    /// never be hijacked this way, and <see cref="CreatureProbe"/> already has a graceful
    /// "carry ended unexpectedly -> Aborted" fallback for exactly this situation. No forced/unsafe
    /// release, no new cancel framework - both paths are calls to APIs that already existed for this.
    ///
    /// Each mode's own attempt still only occupies the shared <see cref="CreaturePickup"/> for one carry
    /// at a time; the state machine returns to Armed (not Terminal) after either mode concludes, so a
    /// single playtest session can keep surfacing HitResponse repeatedly (and Imitative once).
    ///
    /// De-dup (no double-reaction to one Player throw): architecturally a single watched throw in
    /// <see cref="CreatureObservation"/> resolves to EXACTLY ONE of HIT or MISS, never both (it is removed
    /// from <c>_watchedThrows</c> the moment either fires) - so a HIT observation can never also be the
    /// observation that increments <see cref="CreaturePattern"/>'s repeated-miss-near counter (that counter
    /// explicitly excludes <c>ContactedCreature</c>). One throw's resolution therefore can never be the
    /// direct, same-instant cause of both an Imitative latch and a HitResponse edge. The remaining risk is
    /// an OLDER, already-latched Imitative condition (from earlier, unrelated throws) sitting armed at the
    /// exact moment a NEW hit occurs - <see cref="TickArmed"/> resolves that by checking HitResponse first
    /// every frame, so it always wins the commit for that tick; no new event-identity framework was added
    /// for this, since the existing HIT/MISS mutual exclusion already makes the specific-throw case moot.
    ///
    /// Perception provenance (see the project's Player-first / Creature-subjective-knowledge principle):
    /// HitResponse's provenance IS guaranteed - <see cref="CreatureObservation"/>'s class doc confirms
    /// <c>PlayerInteractor</c> is the ONLY raiser of <c>PhysicalEvents</c>, so <c>ThrowHitSerial</c> can
    /// only ever fire for a Player-origin throw physically contacting the creature; no visual perception of
    /// the throw's start is required or claimed for HitResponse. Imitative's trigger, however, is inherited
    /// UNCHANGED from the existing pattern pipeline, which is a global <c>PhysicalEvents.Raised</c>
    /// listener with NO FOV/LOS gate against <see cref="CreaturePerception"/> (see <c>CreatureObservation</c>'s
    /// own "no FOV/witness check" doc) - so Imitative can currently latch off a Player throw the creature
    /// never actually perceived. This is a pre-existing 0.1 characteristic of the shared Observation/Pattern
    /// pipeline (also feeds Memory and the Place probe), not something introduced here; fixing it would mean
    /// redesigning that shared pipeline, out of scope for this throw-reaction-only pass. Flagged, not fixed.
    ///
    /// State flow (0.3): Armed -> Fetching -> Holding -> Finishing -> Armed -> ... (repeats)
    ///   Armed     : waiting for a trigger (HitResponse checked first), then out the deliberation delay
    ///   Fetching  : CreaturePickup is running the grab
    ///   Holding   : object in hand, standing by until the Player is perceived and within range.
    ///               No timeout - holds indefinitely. Gaze is forced to the Player here on (honoured
    ///               only while the Player is actually perceived).
    ///   Finishing : CreaturePickup is running the throw (or a give-up set-down)
    ///   Terminal  : defined but no longer reached in normal play - HitResponse is repeatable forever
    ///               (cooldown-gated, see above), so Terminate() always returns to Armed now. Only
    ///               Imitative is still ever "spent" (one-shot), which alone no longer ends the session.
    ///
    /// Reuse: grab / throw -> <see cref="CreaturePickup"/> (RequestProbeGrab / RequestProbeReleaseAsThrow);
    /// looking at the Player -> <see cref="CreaturePerception"/> (SetForcePlayerGaze); the HIT edge ->
    /// <see cref="CreatureObservation"/> (ThrowHitSerial / LastHitInteractable), read the same
    /// snapshot-and-compare way <c>PlayerSalience</c> already reads it. The creature's own throw does not
    /// raise PhysicalEvents, so it cannot re-trigger either path.
    /// </summary>
    [RequireComponent(typeof(CreaturePattern))]
    [RequireComponent(typeof(CreaturePickup))]
    [RequireComponent(typeof(CreaturePerception))]
    public class CreatureThrowProbe : MonoBehaviour
    {
        public enum ProbeState { Armed, Fetching, Holding, Finishing, Terminal }
        public enum ProbeOutcome { None, Success, Aborted }

        /// <summary>Which physical trigger armed the current/most recent attempt. Naming only - no meaning attached.</summary>
        public enum ThrowProbeMode { Imitative, HitResponse }

        [Header("Imitative trigger (repeated throw-miss-near pattern)")]
        [Tooltip("After Pattern B latches, wait a random time in this range before grabbing. Keeps 'detected' from reading as instant obedience.")]
        [SerializeField] private float probeDelayMin = 2f;
        [SerializeField] private float probeDelayMax = 5f;

        [Header("Hit Response trigger (a Player throw actually hit the creature)")]
        [Tooltip("Sibling CreatureObservation, whose ThrowHitSerial/LastHitInteractable is the trigger. Auto-found if left empty.")]
        [SerializeField] private CreatureObservation observation;
        [Tooltip("After a NEW throw-hit edge, wait a random time in this range before grabbing - shorter than the Imitative delay, since being hit is already a strong, immediate stimulus (see PlayerSalience.throwHitBonus).")]
        [SerializeField] private float hitResponseDelayMin = 0.4f;
        [SerializeField] private float hitResponseDelayMax = 1.2f;

        [Header("Hit Response cooldown (0.3 - repeatable reaction, not a one-shot)")]
        [Tooltip("After a HitResponse attempt concludes (Success or Aborted), a random cooldown in this range is sampled - no NEW HitResponse can arm until it elapses. ThrowHit itself still always reaches PlayerSalience/CreatureObservation during cooldown; only the throw-back behaviour is gated.")]
        [SerializeField] private float hitResponseCooldownMin = 6f;
        [SerializeField] private float hitResponseCooldownMax = 10f;
        [Tooltip("Seconds a pending HitResponse (armed but not yet able to commit, e.g. the creature is busy elsewhere) stays valid. A newer hit refreshes this same pending slot rather than queuing a second one. If it still cannot commit within this time, it quietly expires - no throw-back, but the Salience/Observation record already happened and is untouched.")]
        [SerializeField] private float hitResponsePendingLifetime = 5f;

        [Header("Throw")]
        [Tooltip("From Holding, the probe throws once the player is perceived AND within this flat XZ distance. Shared by both modes. Raised in 0.3.2 (was 4) so HitResponse's ballistic solve actually gets exercised at the mid/long ranges it is meant to reach - keep at or below CreaturePerception.perceptionRange.")]
        [SerializeField] private float throwProbeRange = 9f;
        [Tooltip("Seconds to wait for the grab to actually start once fetching. If it does not, drop back to Armed (this is 'not yet', not a spent attempt).")]
        [SerializeField] private float fetchTimeout = 6f;

        [Header("HitResponse ballistic throw (0.3.2 - flight-time based launch velocity)")]
        [Tooltip("Assumed horizontal travel speed (m/s), used only to pick a flight time from the horizontal distance (flightTime = distance / this, clamped below) - NOT the final launch speed, which also depends on height difference. Higher = flatter, quicker-arriving throws.")]
        [SerializeField] private float hitResponsePreferredHorizontalSpeed = 10f;
        [Tooltip("Flight time is clamped to this range so point-blank hits don't blow up the vertical-velocity formula (near-zero flight time) and far ones don't get an absurdly long, floaty arc.")]
        [SerializeField] private float hitResponseMinFlightTime = 0.25f;
        [SerializeField] private float hitResponseMaxFlightTime = 1.2f;
        [Tooltip("Hard clamp on the computed launch speed (m/s). At long distance/height the solved velocity may exceed this - the clamp wins and the creature simply falls short. Not a perfect throwing machine by design.")]
        [SerializeField] private float hitResponseMaxLaunchSpeed = 18f;

        [Header("Aim (blend between the creature's own facing and the flat toPlayer direction, then scattered)")]
        [Tooltip("Imitative: how much the flat toPlayer direction pulls the throw, 0=ignores the player entirely (throws along the creature's own facing) 1=aimed straight at the player. Low by design - Player direction contribution should read as weak.")]
        [Range(0f, 1f)]
        [SerializeField] private float imitativePlayerAimWeight = 0.35f;
        [Tooltip("Imitative: random yaw (degrees, +/-) applied on top of the weighted aim direction. Wide - a loose playerward toss, not aimed to reliably hit.")]
        [SerializeField] private float imitativeScatterDegrees = 60f;
        [Tooltip("UNUSED for HitResponse since 0.3.2 (its ballistic solve aims directly at the target, no partial blend - see ComputeBallisticVelocity). Kept only so the field/value is not silently lost; harmless if HitResponse never reads it.")]
        [Range(0f, 1f)]
        [SerializeField] private float hitResponsePlayerAimWeight = 0.95f;
        [Tooltip("HitResponse: random yaw (degrees, +/-) applied to the horizontal aim direction before the ballistic vertical solve (see ComputeBallisticVelocity) - small, close to a direct throw at the player.")]
        [SerializeField] private float hitResponseScatterDegrees = 6f;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private ProbeState state = ProbeState.Armed;
        [SerializeField] private ProbeOutcome outcome = ProbeOutcome.None;
        [Tooltip("Which trigger the current/most recent attempt ran as.")]
        [SerializeField] private ThrowProbeMode lastMode = ThrowProbeMode.Imitative;
        [Tooltip("Outcome of the Imitative attempt this session (None until it has run).")]
        [SerializeField] private ProbeOutcome imitativeOutcome = ProbeOutcome.None;
        [Tooltip("Outcome of the HitResponse attempt this session (None until it has run).")]
        [SerializeField] private ProbeOutcome hitResponseOutcome = ProbeOutcome.None;
        [Tooltip("The object this probe locked onto - snapshot of CreaturePattern.LastThrowMissNearObject (Imitative), CreatureObservation.LastHitInteractable (HitResponse), or the stolen CreatureProbe.ProbeObject (HitResponse priority steal) at commit.")]
        [SerializeField] private Interactable probeObject;
        [Tooltip("Seconds spent in the current ProbeState.")]
        [SerializeField] private float stateTimer;
        [Tooltip("0.3 debug: seconds remaining before a new HitResponse can arm. 0 = off cooldown.")]
        [SerializeField] private float hitResponseCooldownRemaining;
        [Tooltip("0.3 debug: seconds the current pending HitResponse (if any) has been waiting to commit.")]
        [SerializeField] private float hitResponsePendingAge;
        [Tooltip("0.3 debug: Time.time of the most recent NEW throw-hit edge seen. -1 before the first one.")]
        [SerializeField] private float lastHitTime = -1f;

        /// <summary>Current phase of the throw probe.</summary>
        public ProbeState State => state;

        /// <summary>None while not finished; Success if the object was thrown toward the Player; Aborted otherwise. Reflects the most recent attempt.</summary>
        public ProbeOutcome Outcome => outcome;

        /// <summary>Which trigger the current/most recent attempt ran as.</summary>
        public ThrowProbeMode LastMode => lastMode;

        /// <summary>Outcome of the Imitative attempt this session, or None if it has not run yet.</summary>
        public ProbeOutcome ImitativeOutcome => imitativeOutcome;

        /// <summary>Outcome of the HitResponse attempt this session, or None if it has not run yet.</summary>
        public ProbeOutcome HitResponseOutcome => hitResponseOutcome;

        /// <summary>True while this probe is mid-run (past Armed, not yet Terminal). CreatureProbe checks this so only one probe engages at a time; a finished (Terminal) probe does NOT block the other.</summary>
        public bool IsEngaged => state != ProbeState.Armed && state != ProbeState.Terminal;

        /// <summary>
        /// Debug seam (section 9): which reaction is actually driving the probe RIGHT NOW - null while
        /// Armed/Terminal (nothing running), else the mode currently occupying Fetching/Holding/Finishing.
        /// Distinct from <see cref="LastMode"/>, which keeps the most recently CONCLUDED mode even after
        /// the probe returns to Armed for the other one.
        /// </summary>
        public ThrowProbeMode? CurrentReaction => IsEngaged ? _activeMode : (ThrowProbeMode?)null;

        /// <summary>Debug seam: seconds spent in the current <see cref="ProbeState"/> - a rough "trigger age" while Armed is deliberating, or "time in phase" otherwise.</summary>
        public float StateTimer => stateTimer;

        /// <summary>0.3 debug (section 7): a compact status independent of the shared ProbeState/ProbeOutcome, specifically about the HitResponse reaction's repeatable lifecycle.</summary>
        public enum HitResponseStatus { Ready, Pending, Executing, Cooldown }

        /// <summary>
        /// Executing: HitResponse is the one currently occupying Fetching/Holding/Finishing.
        /// Cooldown: a previous HitResponse concluded and a new one cannot arm yet.
        /// Pending: armed by a hit, waiting for a chance to commit (busy elsewhere, or still in its
        /// deliberation delay).
        /// Ready: none of the above - the next ThrowHit can arm one immediately.
        /// </summary>
        public HitResponseStatus CurrentHitResponseStatus
        {
            get
            {
                if (IsEngaged && _activeMode == ThrowProbeMode.HitResponse) return HitResponseStatus.Executing;
                if (hitResponseCooldownRemaining > 0f) return HitResponseStatus.Cooldown;
                if (_hitResponseArmed) return HitResponseStatus.Pending;
                return HitResponseStatus.Ready;
            }
        }

        /// <summary>Seconds remaining before a new HitResponse can arm. 0 while off cooldown.</summary>
        public float HitResponseCooldownRemaining => Mathf.Max(0f, hitResponseCooldownRemaining);

        /// <summary>Seconds remaining before the current pending HitResponse expires, or 0 when none is pending.</summary>
        public float HitResponsePendingLifetimeRemaining =>
            _hitResponseArmed ? Mathf.Max(0f, hitResponsePendingLifetime - hitResponsePendingAge) : 0f;

        /// <summary>Seconds since the most recent NEW throw-hit edge, or -1 before the first one this session.</summary>
        public float LastHitAge => lastHitTime >= 0f ? Time.time - lastHitTime : -1f;

        /// <summary>True while the shared CreaturePickup is occupied by ANY carry (this probe's, the Place Probe's, or the creature's own autonomous one) - the reason a Ready/Pending HitResponse cannot commit yet.</summary>
        public bool PickupBusy => _pickup != null && !_pickup.IsIdle;

        private CreaturePattern _pattern;
        private CreaturePickup _pickup;
        private CreaturePerception _perception;
        private CreatureProbe _placeProbe; // optional sibling

        private float _armDelay;
        private bool _delayRolled;
        private ThrowProbeMode _armingMode;  // which mode _armDelay/_delayRolled currently belong to
        private ThrowProbeMode _activeMode;  // which mode the in-progress Fetching/Holding/Finishing attempt is running as
        private ProbeOutcome _pendingOutcome;

        private bool _imitativeUsed; // Imitative stays one-shot-per-session - see class doc for why
        private bool _hitResponseArmed; // latched true on a new ThrowHitSerial edge; cleared on commit, steal, or expiry
        private int _lastSeenHitSerial;

        private void Awake()
        {
            _pattern = GetComponent<CreaturePattern>();
            _pickup = GetComponent<CreaturePickup>();
            _perception = GetComponent<CreaturePerception>();
            _placeProbe = GetComponent<CreatureProbe>();

            if (observation == null)
                observation = GetComponent<CreatureObservation>();
            if (observation != null)
                _lastSeenHitSerial = observation.ThrowHitSerial; // only NEW hits after this component starts count
        }

        private void Update()
        {
            // Detect a NEW throw-hit edge every frame regardless of state (same snapshot-and-compare
            // idiom PlayerSalience uses), so it is not missed while the other mode's attempt is running.
            // A newer hit REFRESHES the pending slot (age resets) rather than queuing a second one -
            // this IS the "only one HitResponse ever pending" rule (section 6): there is exactly one
            // _hitResponseArmed flag, so a second edge before the first commits just resets its clock.
            if (observation != null && observation.ThrowHitSerial != _lastSeenHitSerial)
            {
                _lastSeenHitSerial = observation.ThrowHitSerial;
                lastHitTime = Time.time;
                if (hitResponseCooldownRemaining <= 0f)
                {
                    _hitResponseArmed = true;
                    hitResponsePendingAge = 0f;
                }
            }

            if (hitResponseCooldownRemaining > 0f)
                hitResponseCooldownRemaining = Mathf.Max(0f, hitResponseCooldownRemaining - Time.deltaTime);

            // Pending expiry: only ages while armed-and-not-yet-executing (frozen during this component's
            // OWN HitResponse execution - see the IsEngaged exclusion - so a hit that arrives mid-throw
            // does not expire while we are literally still acting on the earlier one).
            bool executingHitResponse = IsEngaged && _activeMode == ThrowProbeMode.HitResponse;
            if (_hitResponseArmed && !executingHitResponse)
            {
                hitResponsePendingAge += Time.deltaTime;
                if (hitResponsePendingAge >= hitResponsePendingLifetime)
                {
                    _hitResponseArmed = false;
                    hitResponsePendingAge = 0f;
                    Debug.Log("[CreatureThrowProbe] HitResponse pending EXPIRED - could not commit within lifetime (creature stayed busy elsewhere)");
                }
            }

            bool hitResponseReady = _hitResponseArmed && hitResponseCooldownRemaining <= 0f;

            // Priority upgrade (0.3): our OWN in-progress Imitative attempt, still pre-release, becomes
            // a HitResponse in place - same carry, same object, just a different aim recipe from here on.
            if (hitResponseReady && _activeMode == ThrowProbeMode.Imitative
                && (state == ProbeState.Fetching || state == ProbeState.Holding))
            {
                _activeMode = ThrowProbeMode.HitResponse;
                _hitResponseArmed = false;
                hitResponsePendingAge = 0f;
                Debug.Log("[CreatureThrowProbe] Imitative attempt upgraded to HitResponse (higher-priority stimulus arrived mid-attempt)");
            }

            // Priority steal (0.3): the Place Probe already owns an active, probe-carried object (past
            // its own grab). Redirect that carry's release into an aimed HitResponse throw. Safe: only
            // possible because CreaturePickup.RequestProbeReleaseAsThrow refuses anything that is not a
            // probe-owned carry, and CreatureProbe already treats "carry ended unexpectedly" as a normal
            // Aborted outcome - nothing here forces or corrupts state.
            else if (hitResponseReady && state == ProbeState.Armed && _pickup != null && _pickup.IsCarrying
                     && _placeProbe != null
                     && (_placeProbe.State == CreatureProbe.ProbeState.Holding || _placeProbe.State == CreatureProbe.ProbeState.Delivering))
            {
                _activeMode = ThrowProbeMode.HitResponse;
                probeObject = _placeProbe.ProbeObject;
                _hitResponseArmed = false;
                hitResponsePendingAge = 0f;
                _perception.SetForcePlayerGaze(true);
                state = ProbeState.Holding;
                stateTimer = 0f;
                Debug.Log("[CreatureThrowProbe] HitResponse claimed the Place Probe's in-progress carry (higher priority)");
            }

            switch (state)
            {
                case ProbeState.Armed:     TickArmed();     break;
                case ProbeState.Fetching:  TickFetching();  break;
                case ProbeState.Holding:   TickHolding();   break;
                case ProbeState.Finishing: TickFinishing(); break;
                case ProbeState.Terminal:                   break;
            }
        }

        private void TickArmed()
        {
            // Priority: HitResponse (being hit is a stronger, higher-priority stimulus) over Imitative.
            bool wantHitResponse = _hitResponseArmed && hitResponseCooldownRemaining <= 0f;
            bool wantImitative = !wantHitResponse
                                  && _pattern != null && _pattern.RepeatedThrowMissNearDetected
                                  && !_imitativeUsed;

            if (!wantHitResponse && !wantImitative)
            {
                _delayRolled = false;
                return;
            }

            ThrowProbeMode wantedMode = wantHitResponse ? ThrowProbeMode.HitResponse : ThrowProbeMode.Imitative;

            // A more urgent trigger (HitResponse) arriving mid-deliberation restarts the delay under
            // its own timing rather than waiting out whatever was already rolled.
            if (!_delayRolled || _armingMode != wantedMode)
            {
                _armingMode = wantedMode;
                _armDelay = wantedMode == ThrowProbeMode.HitResponse
                    ? Random.Range(hitResponseDelayMin, hitResponseDelayMax)
                    : Random.Range(probeDelayMin, probeDelayMax);
                stateTimer = 0f;
                _delayRolled = true;
            }

            stateTimer += Time.deltaTime;
            if (stateTimer < _armDelay)
                return;

            // --- commit gate: no player distance, just "can we grab that object right now" ---
            if (_pickup == null || _perception == null)
                return;

            if (!_pickup.IsIdle)
                return; // don't hijack a pickup/carry the creature started on its own

            if (_placeProbe != null && _placeProbe.IsEngaged)
                return; // the place probe is mid-run this session - stand down (a finished one does not block)

            Interactable snapshot = wantedMode == ThrowProbeMode.HitResponse
                ? observation.LastHitInteractable
                : _pattern.LastThrowMissNearObject;
            if (snapshot == null || snapshot.Body == null || snapshot.IsHeld)
                return; // the object that formed the trigger is not available - wait, no fallback

            probeObject = snapshot;
            _activeMode = wantedMode;
            if (_pickup.RequestProbeGrab(probeObject))
            {
                if (wantedMode == ThrowProbeMode.HitResponse)
                {
                    _hitResponseArmed = false; // consumed - now executing, not merely pending
                    hitResponsePendingAge = 0f;
                }
                state = ProbeState.Fetching;
                stateTimer = 0f;
            }
        }

        private void TickFetching()
        {
            stateTimer += Time.deltaTime;

            if (_pickup.IsCarrying)
            {
                _perception.SetForcePlayerGaze(true);
                state = ProbeState.Holding;
                stateTimer = 0f;
                return;
            }

            if ((_pickup.IsIdle && stateTimer > 0.1f) || stateTimer >= fetchTimeout)
                AbortToArmed();
        }

        // Object in hand. Wait indefinitely until the player is perceived and near enough, then throw.
        private void TickHolding()
        {
            stateTimer += Time.deltaTime;

            if (!_pickup.IsCarrying)
            {
                Terminate(ProbeOutcome.Aborted, "carry ended unexpectedly");
                return;
            }

            Transform player = _perception.Player;
            if (player == null)
            {
                EnterFinishing(ProbeOutcome.Aborted); // drop it where it stands, abort
                return;
            }
            if (!_perception.IsPlayerPerceived)
                return; // keep holding

            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.magnitude > throwProbeRange)
                return; // keep holding until the player is close enough for a plausible throw (range gate stays player-root/flat, unchanged)

            bool hitResponse = _activeMode == ThrowProbeMode.HitResponse;
            Vector3 launch;
            if (hitResponse)
            {
                Transform aimTarget = _perception.PlayerGazeTarget != null ? _perception.PlayerGazeTarget : player;
                LastHitResponseAimSource = (aimTarget != null && aimTarget != player)
                    ? HitResponseAimSource.Camera
                    : HitResponseAimSource.PlayerRootFallback;
                launch = ComputeBallisticVelocity(transform.position, aimTarget.position);
            }
            else
            {
                launch = ComputeAimDirection(toPlayer); // unchanged 0.2/0.3.1 Imitative direction+scatter
            }

            _pendingOutcome = ProbeOutcome.Success;
            // 0.3.2: HitResponse's `launch` is already a fully-solved, speed-clamped velocity (not just a
            // direction) - preserveVerticalAim tells CreaturePickup to use it verbatim. Imitative still
            // passes a plain direction that CreaturePickup flattens/scales as before.
            _pickup.RequestProbeReleaseAsThrow(launch, preserveVerticalAim: hitResponse);
            state = ProbeState.Finishing;
            stateTimer = 0f;
        }

        /// <summary>0.3.1 debug (section 8): which point HitResponse last aimed at. Unused/stale while Imitative is running.</summary>
        public enum HitResponseAimSource { PlayerRootFallback, Camera }

        /// <summary>0.3.1 debug: PlayerRootFallback until the first HitResponse throw runs, then whichever source that throw actually used.</summary>
        public HitResponseAimSource LastHitResponseAimSource { get; private set; }

        /// <summary>0.3.2 debug: flat distance (m) to the last HitResponse aim target at the moment of the throw.</summary>
        public float LastHitResponseTargetDistance { get; private set; }

        /// <summary>0.3.2 debug: solved flight time (s) for the last HitResponse throw.</summary>
        public float LastHitResponseFlightTime { get; private set; }

        /// <summary>0.3.2 debug: final launch speed (m/s) for the last HitResponse throw, after the max-speed clamp.</summary>
        public float LastHitResponseLaunchSpeed { get; private set; }

        // Imitative-only (unchanged since 0.2/0.3.1): blend the creature's own flat facing (weight 0 -
        // the player is ignored) toward the flat toPlayer direction (weight 1 - aimed at the player),
        // then scatter by a random yaw. Low weight + wide scatter - a loose, unreliable playerward toss.
        // HitResponse no longer uses this - see ComputeBallisticVelocity.
        private Vector3 ComputeAimDirection(Vector3 toPlayerFlat)
        {
            Vector3 targetDir = toPlayerFlat.sqrMagnitude > 1e-6f ? toPlayerFlat.normalized : transform.forward;

            Vector3 facing = transform.forward;
            facing.y = 0f;
            Vector3 facingDir = facing.sqrMagnitude > 1e-6f ? facing.normalized : targetDir;

            Vector3 blended = Vector3.Slerp(facingDir, targetDir, imitativePlayerAimWeight);
            return Quaternion.AngleAxis(Random.Range(-imitativeScatterDegrees, imitativeScatterDegrees), Vector3.up) * blended;
        }

        // 0.3.2 - HitResponse only: flight-time based ballistic solve, not a general physics/interception
        // solver. Aims at the target's CURRENT position only - no player-velocity lead, no homing, no
        // mid-flight correction (section 7). Order matters (section 5): the horizontal DIRECTION is
        // scattered FIRST, then the vertical velocity is solved from the unscattered distance/height so
        // scatter never corrupts the vertical solution (a yaw-only rotation cannot touch Y anyway, but
        // solving vertical from the pre-scatter distance keeps the two axes independent and easy to
        // reason about). The result is clamped to hitResponseMaxLaunchSpeed; if that clamp bites, the
        // throw simply falls short - no re-solve, no compensation. This creature is not a ballistic
        // computer, just "aims honestly at where the Player's camera currently is".
        private Vector3 ComputeBallisticVelocity(Vector3 origin, Vector3 target)
        {
            Vector3 horizontal = target - origin;
            horizontal.y = 0f;
            float horizontalDistance = horizontal.magnitude;
            LastHitResponseTargetDistance = horizontalDistance;

            Vector3 horizontalDir = horizontalDistance > 1e-6f ? horizontal / horizontalDistance : transform.forward;
            horizontalDir = Quaternion.AngleAxis(Random.Range(-hitResponseScatterDegrees, hitResponseScatterDegrees), Vector3.up) * horizontalDir;

            float flightTime = Mathf.Clamp(horizontalDistance / Mathf.Max(0.01f, hitResponsePreferredHorizontalSpeed),
                hitResponseMinFlightTime, hitResponseMaxFlightTime);
            LastHitResponseFlightTime = flightTime;

            Vector3 horizontalVelocity = horizontalDir * (horizontalDistance / flightTime);

            float heightDiff = target.y - origin.y;
            float gravityY = Physics.gravity.y; // negative
            float verticalVelocity = (heightDiff - 0.5f * gravityY * flightTime * flightTime) / flightTime;

            Vector3 launch = horizontalVelocity + Vector3.up * verticalVelocity;
            float speed = launch.magnitude;
            if (speed > hitResponseMaxLaunchSpeed && speed > 1e-6f)
                launch *= hitResponseMaxLaunchSpeed / speed;

            LastHitResponseLaunchSpeed = launch.magnitude;
            return launch;
        }

        // Throw (or give-up set-down) is running in CreaturePickup. Commit the outcome once it is back
        // to Idle. Gaze stays forced to the player through this.
        private void TickFinishing()
        {
            stateTimer += Time.deltaTime;

            if (_pickup.IsIdle)
            {
                Terminate(_pendingOutcome,
                    _pendingOutcome == ProbeOutcome.Success ? "object thrown toward player" : "object set down on give-up");
                return;
            }

            if (stateTimer >= 5f) // safety net
                Terminate(_pendingOutcome, "finish stalled");
        }

        // Abort while carrying: set the object down via the normal Place path (safe teardown), record Aborted.
        private void EnterFinishing(ProbeOutcome pending)
        {
            _pendingOutcome = pending;
            _pickup.RequestProbeRelease();
            state = ProbeState.Finishing;
            stateTimer = 0f;
        }

        // Pre-commit only: nothing is being carried, so this is not a used-up attempt.
        private void AbortToArmed()
        {
            _perception.SetForcePlayerGaze(false);
            probeObject = null;
            _delayRolled = false;
            state = ProbeState.Armed;
            stateTimer = 0f;
        }

        private void Terminate(ProbeOutcome result, string reason)
        {
            _perception.SetForcePlayerGaze(false);
            outcome = result == ProbeOutcome.None ? ProbeOutcome.Aborted : result;
            lastMode = _activeMode;

            if (_activeMode == ThrowProbeMode.HitResponse)
            {
                hitResponseOutcome = outcome;
                // 0.3: repeatable, not spent - a random cooldown gates the NEXT one instead of a
                // permanent flag. _hitResponseArmed is already false by the time we get here (cleared
                // at commit/upgrade/steal), so there is nothing left to "use up".
                hitResponseCooldownRemaining = Random.Range(hitResponseCooldownMin, hitResponseCooldownMax);
            }
            else
            {
                imitativeOutcome = outcome;
                _imitativeUsed = true; // Imitative stays one-shot-per-session - see class doc
            }

            probeObject = null;
            _delayRolled = false;
            stateTimer = 0f;

            // 0.3: HitResponse is repeatable forever (cooldown only), so Terminal is no longer a normal
            // destination - always return to Armed so a future ThrowHit can arm another attempt even
            // after Imitative's one-shot has been spent.
            state = ProbeState.Armed;

            Debug.Log($"[CreatureThrowProbe] {_activeMode} probe {(outcome == ProbeOutcome.Success ? "SUCCESS" : "ABORTED")} - {reason}" +
                      (_activeMode == ThrowProbeMode.HitResponse ? $" - cooldown {hitResponseCooldownRemaining:F1}s" : ""));
        }

        private void OnValidate()
        {
            probeDelayMin = Mathf.Max(0f, probeDelayMin);
            probeDelayMax = Mathf.Max(probeDelayMin, probeDelayMax);
            hitResponseDelayMin = Mathf.Max(0f, hitResponseDelayMin);
            hitResponseDelayMax = Mathf.Max(hitResponseDelayMin, hitResponseDelayMax);
            hitResponseCooldownMin = Mathf.Max(0f, hitResponseCooldownMin);
            hitResponseCooldownMax = Mathf.Max(hitResponseCooldownMin, hitResponseCooldownMax);
            hitResponsePendingLifetime = Mathf.Max(0.1f, hitResponsePendingLifetime);
            throwProbeRange = Mathf.Max(0f, throwProbeRange);
            fetchTimeout = Mathf.Max(1f, fetchTimeout);
            imitativePlayerAimWeight = Mathf.Clamp01(imitativePlayerAimWeight);
            hitResponsePlayerAimWeight = Mathf.Clamp01(hitResponsePlayerAimWeight);
            imitativeScatterDegrees = Mathf.Clamp(imitativeScatterDegrees, 0f, 90f);
            hitResponseScatterDegrees = Mathf.Clamp(hitResponseScatterDegrees, 0f, 90f);
            hitResponsePreferredHorizontalSpeed = Mathf.Max(0.1f, hitResponsePreferredHorizontalSpeed);
            hitResponseMinFlightTime = Mathf.Max(0.01f, hitResponseMinFlightTime);
            hitResponseMaxFlightTime = Mathf.Max(hitResponseMinFlightTime, hitResponseMaxFlightTime);
            hitResponseMaxLaunchSpeed = Mathf.Max(0.1f, hitResponseMaxLaunchSpeed);
        }
    }
}
