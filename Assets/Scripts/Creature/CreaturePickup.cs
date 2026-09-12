using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Creature Pickup + Release (0.1): after the creature has been inspect-dwelling on a free world
    /// <see cref="Interactable"/> for a little while it reaches ONE primitive arm pivot toward that
    /// object and, at the moment the arm arrives, takes it - parents it to a hold anchor on the
    /// creature and makes its body kinematic - then eases the arm back and carries it. After a random
    /// carry duration it runs one of two interchangeable release capabilities, picked purely by the
    /// dev-only <see cref="releaseMode"/> toggle (never decided by the creature itself):
    /// <list type="bullet">
    /// <item>Place: reach toward a spot on the ground just in front of it and set the object down
    /// there flush, upright.</item>
    /// <item>Throw: reach forward as a throw gesture and give the object a velocity along
    /// <see cref="CreatureBodyExpression.bodyVisual"/>'s own forward (plus a little up for an arc) -
    /// never the gaze/head direction, never aimed at anything.</item>
    /// </list>
    /// Either way it unparents, restores the collider/Rigidbody and releases the claim, then eases the
    /// arm back to rest, free to investigate something else.
    ///
    /// Grab, carry, then place or throw. No handing it to the player, no target, no moving toward a
    /// chosen destination, no reaction to what was carried, and Place/Throw is never chosen at random
    /// or by the creature's situation - <see cref="releaseMode"/> is a fixed Inspector setting for
    /// exercising one capability at a time, not a decision system. Taking an object someone already
    /// holds is deliberately NOT possible here (that is a future Take / Snatch action); pickup only
    /// works on an object whose <see cref="Interactable.Holder"/> is null, claimed atomically via
    /// <see cref="Interactable.TryGrab"/>.
    ///
    /// The pickup target is whatever <see cref="CreatureMovement.InspectTarget"/> is when the grab
    /// starts; it is captured once into <see cref="_target"/> so a later Attention change cannot make
    /// the arm swing to a different object mid-grab, mid-carry or mid-release. Like
    /// <see cref="CreaturePhysicalProbe"/> this poses its arm in <see cref="LateUpdate"/> only while a
    /// grab or release animation runs, so <see cref="CreatureBodyExpression"/> keeps full control of
    /// every limb the rest of the time. It uses a different arm from the probe, so the two never
    /// fight. Gaze / head / pupil, Approach / Retreat / Inspect movement and the walk swing are all
    /// read-only here or untouched.
    /// </summary>
    [RequireComponent(typeof(CreatureMovement))]
    public class CreaturePickup : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Arm pivot that does the grabbing - an empty transform at a shoulder, child of BodyVisual. Its local -Y (the visual arm) is aimed at the object. Use the arm the Physical Probe does NOT use.")]
        [SerializeField] private Transform grabArm;
        [Tooltip("Empty transform the taken object is parented to (e.g. in front of the chest). Child of BodyVisual so it follows body orientation.")]
        [SerializeField] private Transform holdAnchor;
        [Tooltip("Sibling CreatureMovement. Read only, for its inspect state and target. Auto-found if left empty.")]
        [SerializeField] private CreatureMovement movement;

        [Header("Release mode - DEV TEST TOGGLE ONLY")]
        [Tooltip("Which release capability to exercise when a carry ends: Place (set down in front) or Throw (BodyVisual-forward velocity). A fixed Inspector setting for testing one capability at a time - NOT read by any Attention/Decision logic, and the creature never chooses between them itself.")]
        [SerializeField] private ReleaseMode releaseMode = ReleaseMode.Place;

        [Header("When to grab")]
        [Tooltip("Minimum seconds of inspect-dwell on one free, in-reach object before the creature takes it.")]
        [SerializeField] private float pickupDelayMin = 5f;
        [Tooltip("Maximum seconds of inspect-dwell on one free, in-reach object before the creature takes it.")]
        [SerializeField] private float pickupDelayMax = 9f;
        [Tooltip("Only grab when the object's flat XZ distance is within this. Keep at or above the movement's approachStopDistance.")]
        [SerializeField] private float grabRange = 1.9f;

        [Header("Arm motion")]
        [Tooltip("Seconds for the arm to swing out from rest to the object.")]
        [SerializeField] private float reachDuration = 0.22f;
        [Tooltip("Seconds the arm stays on the object. The take happens as this begins.")]
        [SerializeField] private float holdDuration = 0.08f;
        [Tooltip("Seconds for the arm to ease back from the object to rest.")]
        [SerializeField] private float returnDuration = 0.3f;
        [Tooltip("How far toward a straight point-at-the-object aim the arm goes. 1 = arm points right at it.")]
        [Range(0f, 1f)]
        [SerializeField] private float reachAmount = 0.9f;

        [Header("Carry / Release (shared by Place and Throw)")]
        [Tooltip("Minimum seconds to keep carrying before releasing the object.")]
        [SerializeField] private float carryDurationMin = 4f;
        [Tooltip("Maximum seconds to keep carrying before releasing the object.")]
        [SerializeField] private float carryDurationMax = 8f;
        [Tooltip("Seconds for the arm to reach out as the release gesture. The release itself happens as this completes.")]
        [SerializeField] private float releaseReachDuration = 0.3f;
        [Tooltip("Seconds for the arm to ease back up to rest after releasing.")]
        [SerializeField] private float releaseReturnDuration = 0.3f;

        [Header("Place (used only when Release Mode = Place)")]
        [Tooltip("How far in front of the creature (flat XZ, along BodyVisual forward) the object is set down.")]
        [SerializeField] private float placeForwardDistance = 0.9f;
        [Tooltip("The downward floor search for a place spot starts this high above the creature root.")]
        [SerializeField] private float placeRayStartHeight = 1.5f;
        [Tooltip("Max downward search distance for the floor.")]
        [SerializeField] private float placeRayDistance = 3f;
        [Tooltip("Layers considered ground when searching for a place spot.")]
        [SerializeField] private LayerMask placeMask = ~0;

        [Header("Throw (used only when Release Mode = Throw)")]
        [Tooltip("Speed given to the object along BodyVisual's forward direction, in m/s - sets velocity directly (like PlayerInteractor.Throw), not an impulse.")]
        [SerializeField] private float throwForce = 6f;
        [Tooltip("Fraction of straight-up blended into BodyVisual forward before normalizing, for a natural arc instead of a flat line. 0 = flat, higher = steeper.")]
        [SerializeField] private float throwUpwardFraction = 0.35f;

        [Header("Debug (read-only - observation only, never read by logic below)")]
        [Tooltip("Current pickup phase.")]
        [SerializeField] private Phase debugPhase;
        [Tooltip("CreaturePerception.AttendedInteractable right now - the raw nearest thing, before Movement/Pickup filter it.")]
        [SerializeField] private Interactable debugAttentionInteractable;
        [Tooltip("Mirrors _dwellTarget - the object the investigation clock is currently counting for.")]
        [SerializeField] private Interactable debugDwellTarget;
        [Tooltip("Mirrors _dwellAccum - seconds banked toward the current threshold.")]
        [SerializeField] private float debugDwellAccum;
        [Tooltip("Mirrors _threshold - this attempt's random pickupDelayMin..Max.")]
        [SerializeField] private float debugThreshold;
        [Tooltip("movement.IsInspecting this frame.")]
        [SerializeField] private bool debugIsInspecting;
        [Tooltip("movement.IsInspectDwelling this frame.")]
        [SerializeField] private bool debugIsInspectDwelling;
        [Tooltip("Flat distance to the relevant object: _target while Reaching/Holding/Returning/Carrying, otherwise the current TickIdle candidate. -1 when there is none.")]
        [SerializeField] private float debugDistanceToTarget;
        [Tooltip("Mirrors the grabRange setting above, shown next to the distance for easy comparison.")]
        [SerializeField] private float debugGrabRange;
        [Tooltip("IsHeld of that same relevant object.")]
        [SerializeField] private bool debugTargetIsHeld;
        [Tooltip("True exactly when TickIdle's own firing condition (dwell time reached, settled, in range) evaluated true this frame - computed from the identical expression TickIdle uses, so this is never a guess.")]
        [SerializeField] private bool debugReadyToPickup;

        // DEV TEST TOGGLE ONLY - see the tooltip above. Not a decision system.
        private enum ReleaseMode { Place, Throw }

        private enum Phase { Idle, Reaching, Holding, Returning, Carrying, Releasing, ReleaseReturning }

        // --- CreatureProbe seams --------------------------------------------------------------------
        // Read-only phase probes and two commands. Everything a probe needs to reuse the existing
        // grab -> carry -> Place pipeline without any of the grab/carry/release logic changing.

        /// <summary>True when nothing is being grabbed, carried or released - safe for a probe to start.</summary>
        public bool IsIdle => _phase == Phase.Idle;

        /// <summary>True while an object is being carried (post-grab, pre-release).</summary>
        public bool IsCarrying => _phase == Phase.Carrying;

        /// <summary>True when a release right now would Place (set down), not Throw. CreatureProbe refuses to run otherwise.</summary>
        public bool ReleaseModeIsPlace => releaseMode == ReleaseMode.Place;

        /// <summary>
        /// CreatureProbe only: if Idle and <paramref name="target"/> is a free, in-range object, claim it
        /// and start the normal grab (Reaching -> ... -> Carrying). Returns whether the grab started. The
        /// carry that follows is flagged so its automatic time-out release is suppressed - the probe
        /// ends it with <see cref="RequestProbeRelease"/>.
        /// </summary>
        public bool RequestProbeGrab(Interactable target)
        {
            if (_phase != Phase.Idle)
                return false;
            if (target == null || target.Body == null || target.IsHeld)
                return false;
            if (FlatDistance(target.transform.position) > grabRange)
                return false;
            if (!target.TryGrab(this))
                return false;

            _target = target;
            _dwellTarget = null;
            _dwellAccum = 0f;
            _aborted = false;
            _probeCarry = true;
            _phase = Phase.Reaching;
            _phaseTimer = 0f;
            return true;
        }

        /// <summary>
        /// Probe only: end the current probe carry now - the object is set down via the exact same
        /// Releasing -> PlaceTarget -> ReleaseReturning path a normal carry uses. No-op unless a probe
        /// carry is in progress.
        /// </summary>
        public void RequestProbeRelease()
        {
            if (_phase != Phase.Carrying || !_probeCarry)
                return;

            _probeCarry = false;          // re-enable the normal "carry time up" check...
            _carryTimer = _carryThreshold; // ...and make it fire on the next Update tick
        }

        /// <summary>
        /// CreatureThrowProbe only: end the current probe carry now as an AIMED Throw along
        /// <paramref name="aimDir"/> (typically the vector to the player), regardless of the dev
        /// <c>releaseMode</c> toggle. Goes through the same Releasing -> ThrowTarget -> ReleaseReturning
        /// path a dev-toggle Throw uses; only the direction source changes. No-op unless a probe carry
        /// is in progress.
        /// </summary>
        /// <param name="aimDir">
        /// By default (<paramref name="preserveVerticalAim"/> = false) a world DIRECTION toward the aim
        /// target: BeginReleasing zeroes Y, normalizes, then blends in <see cref="throwUpwardFraction"/>
        /// of straight-up for the usual toss arc and scales by <see cref="throwForce"/> - unchanged 0.2
        /// behaviour, still what Imitative uses. When <paramref name="preserveVerticalAim"/> is true this
        /// is instead the already-solved, already-speed-clamped final launch VELOCITY (0.3.2 ballistic
        /// solve) - used verbatim, no rescale.
        /// </param>
        /// <param name="preserveVerticalAim">
        /// 0.3.2 (HitResponse ballistic aim): when true, <paramref name="aimDir"/> is the caller's own
        /// fully-solved launch velocity (magnitude included) - e.g. CreatureThrowProbe's flight-time
        /// ballistic calculation toward the Player's camera/view point - used as-is instead of being
        /// flattened, normalized, or replaced with the fixed toss-arc blend. This component still does
        /// not perform any ballistic prediction itself; it only refrains from overriding one it is given.
        /// </param>
        public void RequestProbeReleaseAsThrow(Vector3 aimDir, bool preserveVerticalAim = false)
        {
            if (_phase != Phase.Carrying || !_probeCarry)
                return;

            _probeThrowPending = true;
            _probeThrowAimDir = aimDir; // BeginReleasing flattens+guards+normalizes+scales, unless preserveVerticalAim (then used verbatim)
            _probeThrowPreserveVertical = preserveVerticalAim;
            _probeCarry = false;
            _carryTimer = _carryThreshold;
        }

        // True when the release now in progress should Throw (aimed) rather than Place: either the dev
        // toggle is Throw, or CreatureThrowProbe forced an aimed throw for this probe carry.
        private bool ReleasingAsThrow => _probeThrowPending || releaseMode == ReleaseMode.Throw;
        // ------------------------------------------------------------------------------------------

        private Quaternion _restLocalRotation;
        private Phase _phase = Phase.Idle;
        private float _phaseTimer;
        private float _dwellAccum;   // seconds spent inspecting _dwellTarget, banked toward a grab
        private float _threshold;    // this attempt's random pickupDelayMin..Max
        private bool _aborted;       // grab was cut short before the take
        private Interactable _dwellTarget;   // the object the dwell timer is currently counting for
        private Interactable _target;        // the object this grab/carry/release sequence concerns (captured once)
        private Collider[] _targetColliders; // disabled while carried, restored on teardown / on release
        private float _targetPivotToBottom;  // how far _target's pivot sits above its own lowest point (for flush placement)
        private float _carryTimer;           // seconds spent in Carrying so far
        private float _carryThreshold;       // this carry's random carryDurationMin..Max
        private bool _probeCarry;            // true while THIS carry was started by a probe - suppresses the automatic carry-time release so the probe alone decides when to set down
        private bool _probeThrowPending;     // probe carry that must release as an aimed Throw regardless of releaseMode (set by CreatureThrowProbe)
        private Vector3 _probeThrowAimDir;   // world direction for that throw - toward the player (flat) or camera/view point (0.3.1, see _probeThrowPreserveVertical)
        private bool _probeThrowPreserveVertical; // 0.3.1: honour _probeThrowAimDir's own Y instead of flattening + adding throwUpwardFraction (HitResponse only)
        private Vector3 _armAimPoint;        // world point the arm reaches toward during Releasing (Place's ground spot, or a point out along the throw direction)
        private Vector3 _placePosition;      // Place only: world-space spot the object is actually set down at
        private Quaternion _placeRotation;   // Place only: upright, yawed with the creature's own facing
        private Vector3 _throwVelocity;      // Throw only: BodyVisual-forward (+ a little up), scaled by throwForce
        private CreaturePerception _debugPerception; // debug-only: lets the Inspector show raw Attention, independent of what Movement/Pickup filter it to

        private void Awake()
        {
            if (movement == null)
                movement = GetComponent<CreatureMovement>();
            if (grabArm != null)
                _restLocalRotation = grabArm.localRotation;
            _threshold = Random.Range(pickupDelayMin, pickupDelayMax);
            _debugPerception = GetComponent<CreaturePerception>();
        }

        private void Update()
        {
            // Default each frame; TickIdle overwrites it with the real evaluated condition if (and
            // only if) it actually reaches that check this frame. Any other phase correctly reads as
            // "not ready" here since TickIdle isn't even running.
            debugReadyToPickup = false;

            switch (_phase)
            {
                case Phase.Carrying:
                    // Just hold it (parenting does the work) until the random carry time is up, then
                    // hand off to whichever release capability releaseMode currently selects.
                    _carryTimer += Time.deltaTime;
                    // _probeCarry suppresses this automatic release: during a probe carry only
                    // RequestProbeRelease() (which clears the flag and forces the timer) ends it.
                    if (!_probeCarry && _carryTimer >= _carryThreshold)
                        BeginReleasing();
                    break;

                case Phase.Idle:
                    TickIdle();
                    break;

                case Phase.Reaching:
                    // The grab is committed once it starts. It was already claimed with TryGrab in
                    // TickIdle, and that claim is exactly why CreatureMovement drops this object as an
                    // inspect target and IsInspecting goes false the next frame - so IsInspecting is
                    // NOT an abort condition here (that was the "never picks up" bug). The reach is
                    // ~0.2s; only bail if the object itself has gone.
                    if (_target == null || movement == null)
                    {
                        if (_target != null) _target.Release(this);
                        _aborted = true;
                        _phase = Phase.Returning;
                        _phaseTimer = 0f;
                        break;
                    }
                    _phaseTimer += Time.deltaTime;
                    if (_phaseTimer >= reachDuration)
                    {
                        TakeTarget();
                        _phase = Phase.Holding;
                        _phaseTimer = 0f;
                    }
                    break;

                case Phase.Holding:
                    _phaseTimer += Time.deltaTime;
                    if (_phaseTimer >= holdDuration)
                    {
                        _phase = Phase.Returning;
                        _phaseTimer = 0f;
                    }
                    break;

                case Phase.Returning:
                    _phaseTimer += Time.deltaTime;
                    if (_phaseTimer >= returnDuration)
                    {
                        if (_aborted)
                        {
                            _aborted = false;
                            _probeCarry = false;
                            _probeThrowPending = false;
                            _probeThrowPreserveVertical = false;
                            _target = null;
                            _dwellAccum = 0f;
                            _dwellTarget = null;
                            _threshold = Random.Range(pickupDelayMin, pickupDelayMax);
                            _phase = Phase.Idle;
                        }
                        else
                        {
                            _phase = Phase.Carrying;
                            _carryTimer = 0f;
                            _carryThreshold = Random.Range(carryDurationMin, carryDurationMax);
                        }
                        _phaseTimer = 0f;
                    }
                    break;

                case Phase.Releasing:
                    // Object stays put at the hold anchor (still "in hand") while the arm reaches out
                    // as a gesture toward _armAimPoint; the actual release happens all at once at the
                    // end, exactly the way TakeTarget() snaps the object to the hand at contact rather
                    // than animating the pickup - keeps grab and release symmetric.
                    _phaseTimer += Time.deltaTime;
                    if (_phaseTimer >= releaseReachDuration)
                    {
                        if (ReleasingAsThrow)
                            ThrowTarget();
                        else
                            PlaceTarget();
                        _phase = Phase.ReleaseReturning;
                        _phaseTimer = 0f;
                    }
                    break;

                case Phase.ReleaseReturning:
                    _phaseTimer += Time.deltaTime;
                    if (_phaseTimer >= releaseReturnDuration)
                    {
                        // Full reset - ready to bank dwell time toward a fresh pickup again.
                        _probeCarry = false;
                        _probeThrowPending = false;
                        _probeThrowPreserveVertical = false;
                        _target = null;
                        _dwellTarget = null;
                        _dwellAccum = 0f;
                        _threshold = Random.Range(pickupDelayMin, pickupDelayMax);
                        _phase = Phase.Idle;
                        _phaseTimer = 0f;
                    }
                    break;
            }

            // Debug-only: mirrors current state into the inspector fields above. Reads state, writes
            // nothing any logic above reads back, so it cannot change behaviour.
            RefreshDebugView();
        }

        private void TickIdle()
        {
            Interactable candidate = movement != null ? movement.InspectTarget : null;

            // Attention is no longer on a free object we could grab (player / nothing / already held):
            // genuinely give up whatever investigation was in progress.
            if (candidate == null || candidate.Body == null || candidate.IsHeld)
            {
                _dwellTarget = null;
                _dwellAccum = 0f;
                return;
            }

            // Attention actually moved to a DIFFERENT object -> start a fresh clock for that one.
            if (_dwellTarget != null && candidate != _dwellTarget)
            {
                _dwellTarget = null;
                _dwellAccum = 0f;
            }

            // Start banking time only once we are close enough to really be inspecting it - not while
            // still walking across the room toward it the first time.
            if (_dwellTarget == null)
            {
                if (FlatDistance(candidate.transform.position) > grabRange)
                    return;
                _dwellTarget = candidate;
                _dwellAccum = 0f;
                _threshold = Random.Range(pickupDelayMin, pickupDelayMax);
            }

            // Same object we have been investigating. Keep counting through the whole time Attention
            // stays on it - including the brief moments a Physical Probe knocks it and the creature
            // re-approaches (IsInspecting flickers false, distance briefly exceeds the ring). Only a
            // real Attention change (handled above) or taking it ends this count.
            _dwellAccum += Time.deltaTime;

            // Fire once enough time is banked AND we are settled on it AND back in reach. Named local
            // (not inlined) purely so the exact same test can also be mirrored into debugReadyToPickup
            // below - same short-circuit order, same result, nothing behavioural changed.
            bool readyToPickup = _dwellAccum >= _threshold
                                  && movement.IsInspectDwelling
                                  && FlatDistance(candidate.transform.position) <= grabRange;
            debugReadyToPickup = readyToPickup;

            // Claim it atomically; if the player beat us to it, TryGrab fails and the guard at the
            // top of the next tick clears the count.
            if (readyToPickup && candidate.TryGrab(this))
            {
                _target = candidate;
                _aborted = false;
                _phase = Phase.Reaching;
                _phaseTimer = 0f;
            }
        }

        // Debug-only observation: mirrors internal state into the [SerializeField] debug block above
        // so Play Mode Inspector can show it. Called once per Update after the state machine has run
        // its step for the frame; writes only debug* fields, which nothing above ever reads back.
        private void RefreshDebugView()
        {
            debugPhase = _phase;
            debugAttentionInteractable = _debugPerception != null ? _debugPerception.AttendedInteractable : null;

            Interactable candidate = movement != null ? movement.InspectTarget : null;
            debugDwellTarget = _dwellTarget;
            debugDwellAccum = _dwellAccum;
            debugThreshold = _threshold;
            debugIsInspecting = movement != null && movement.IsInspecting;
            debugIsInspectDwelling = movement != null && movement.IsInspectDwelling;
            debugGrabRange = grabRange;

            // The object actually relevant to what's happening right now: while Reaching/Holding/
            // Returning/Carrying that's _target (what we grabbed); otherwise it's whatever TickIdle is
            // currently evaluating. _target is intentionally checked first: once claimed, candidate
            // (movement.InspectTarget) goes null anyway because CreatureMovement excludes held objects.
            Interactable relevant = _target != null ? _target : candidate;
            debugTargetIsHeld = relevant != null && relevant.IsHeld;
            debugDistanceToTarget = relevant != null ? FlatDistance(relevant.transform.position) : -1f;
        }

        // Parent + freeze the object at the hold anchor. Same verified handling PlayerInteractor uses.
        private void TakeTarget()
        {
            if (_target == null)
                return;

            _targetColliders = _target.GetComponentsInChildren<Collider>();
            // While colliders are still enabled, same as PlayerInteractor.Pickup - so later, at Place
            // time, the object's own lowest point can be rested flush on the ground.
            _targetPivotToBottom = ComputePivotToBottom(_target.transform, _targetColliders);
            foreach (var col in _targetColliders)
                if (col != null) col.enabled = false;

            var body = _target.Body;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;

            Transform anchor = holdAnchor != null ? holdAnchor : transform;
            _target.transform.SetParent(anchor, worldPositionStays: false);
            _target.transform.localPosition = _target.HoldPositionOffset;
            _target.transform.localRotation = _target.HoldRotationOffset;
        }

        // Starts the Releasing animation for whichever capability releaseMode currently selects: works
        // out the arm's aim point (and, for Place, the object's actual landing spot) up front, but
        // does not touch the object yet - it stays "in hand" at the hold anchor until PlaceTarget /
        // ThrowTarget runs at the end of the reach.
        private void BeginReleasing()
        {
            if (_target == null)
            {
                _phase = Phase.Idle;
                return;
            }

            // grabArm's own parent is BodyVisual (documented on the grabArm field) - the same facing
            // CreatureBodyExpression yaws toward the travel direction. Deliberately BodyVisual, never
            // the LookPivot/head the gaze system drives.
            Transform facing = grabArm != null && grabArm.parent != null ? grabArm.parent : transform;

            if (ReleasingAsThrow)
            {
                if (_probeThrowPending && _probeThrowPreserveVertical && _probeThrowAimDir.sqrMagnitude > 1e-6f)
                {
                    // 0.3.2 (HitResponse ballistic aim): CreatureThrowProbe already solved the full
                    // launch VELOCITY (not just a direction) via its own flight-time ballistic formula,
                    // toward the Player's camera/view point, already speed-clamped. Use it verbatim -
                    // no flatten, no throwForce rescale, no added toss-arc blend (that would corrupt the
                    // vertical solution this velocity already encodes).
                    _throwVelocity = _probeThrowAimDir;
                    _armAimPoint = grabArm.position + _probeThrowAimDir.normalized * 0.6f;
                }
                else
                {
                    // Dev-toggle Throw uses BodyVisual forward; a probe-forced throw uses the aim direction
                    // CreatureThrowProbe supplied (toward the player). Flatten + guard + normalize the same way.
                    Vector3 forward = _probeThrowPending ? _probeThrowAimDir : facing.forward;
                    forward.y = 0f;
                    if (forward.sqrMagnitude < 1e-6f)
                        forward = Vector3.forward;
                    forward.Normalize();

                    // Forward blended with a slice of straight up, then normalized and scaled - the same
                    // "blend then normalize" idiom CreaturePhysicalProbe already uses for its poke
                    // direction, reused here for a natural arc instead of a flat throw.
                    Vector3 throwDir = (forward + Vector3.up * throwUpwardFraction).normalized;
                    _throwVelocity = throwDir * throwForce;
                    _armAimPoint = grabArm.position + throwDir * 0.6f; // just a gesture target, not the flight path
                }
            }
            else
            {
                _placePosition = ComputePlacePosition(facing);
                _placeRotation = Quaternion.Euler(0f, facing.eulerAngles.y, 0f);
                _armAimPoint = _placePosition;
            }

            _phase = Phase.Releasing;
            _phaseTimer = 0f;
        }

        // Finds a flat spot on the ground a short distance in front of the creature and rests the
        // object's own lowest point on it - the same pivot-to-bottom idea PlayerInteractor.Place uses,
        // just aimed forward from the creature instead of wherever the player is looking. No object or
        // destination selection: always "the ground straight ahead".
        private Vector3 ComputePlacePosition(Transform facing)
        {
            Vector3 forward = facing.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 groundXZ = transform.position + forward * placeForwardDistance;
            Vector3 rayStart = new Vector3(groundXZ.x, transform.position.y + placeRayStartHeight, groundXZ.z);

            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, placeRayDistance, placeMask, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * _targetPivotToBottom;

            // No floor found under the forward spot (shouldn't happen in TestRoom) - fall back to the
            // creature's own foot height so the object at least doesn't float or sink into it.
            return new Vector3(groundXZ.x, transform.position.y, groundXZ.z) + Vector3.up * _targetPivotToBottom;
        }

        // The actual set-down: unparent back into the world at _placePosition, restore collider /
        // Rigidbody exactly like PlayerInteractor.Place does, and release the claim so the object is a
        // normal free Interactable again - Player and Creature can both consider it from here.
        private void PlaceTarget()
        {
            if (_target == null)
                return;

            _target.transform.SetParent(null, worldPositionStays: true);
            _target.transform.SetPositionAndRotation(_placePosition, _placeRotation);

            if (_targetColliders != null)
                foreach (var col in _targetColliders)
                    if (col != null) col.enabled = true;
            _targetColliders = null;

            var body = _target.Body;
            if (body != null)
            {
                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.Sleep();
            }

            _target.Release(this);
            // _target itself is left set until ReleaseReturning finishes: LateUpdate still eases the
            // arm back from (now) _placePosition, and Attention/Movement are free to pick it straight
            // back up as a normal world object since IsHeld is already false.
        }

        // The actual throw: detach it right where it currently is (still near the hand - Throw does
        // not relocate the object the way Place does), restore collider / Rigidbody, and give it
        // _throwVelocity instead of resting it. Mirrors PlayerInteractor.Throw: sets linearVelocity
        // directly (not an impulse) and deliberately does NOT call Rigidbody.Sleep() afterwards.
        private void ThrowTarget()
        {
            if (_target == null)
                return;

            _target.transform.SetParent(null, worldPositionStays: true);

            if (_targetColliders != null)
                foreach (var col in _targetColliders)
                    if (col != null) col.enabled = true;
            _targetColliders = null;

            var body = _target.Body;
            if (body != null)
            {
                body.isKinematic = false;
                body.linearVelocity = _throwVelocity;
                body.angularVelocity = Vector3.zero;
            }

            _target.Release(this);
            // _target left set for the same reason as PlaceTarget - see there. LateUpdate special-cases
            // Throw during ReleaseReturning (below) so the arm doesn't try to chase the flying object.
        }

        // Pose the grab arm for the current phase. LateUpdate so this wins over CreatureBodyExpression's
        // per-frame limb write, but only for the brief stretches a grab or release animation runs.
        private void LateUpdate()
        {
            if (grabArm == null)
                return;
            if (_phase != Phase.Reaching && _phase != Phase.Holding && _phase != Phase.Returning
                && _phase != Phase.Releasing && _phase != Phase.ReleaseReturning)
                return;

            float raw = _phase switch
            {
                Phase.Reaching => reachDuration > 0f ? Mathf.Clamp01(_phaseTimer / reachDuration) : 1f,
                Phase.Holding => 1f,
                Phase.Returning => returnDuration > 0f ? 1f - Mathf.Clamp01(_phaseTimer / returnDuration) : 0f,
                Phase.Releasing => releaseReachDuration > 0f ? Mathf.Clamp01(_phaseTimer / releaseReachDuration) : 1f,
                Phase.ReleaseReturning => releaseReturnDuration > 0f ? 1f - Mathf.Clamp01(_phaseTimer / releaseReturnDuration) : 0f,
                _ => 0f,
            };
            float t = Mathf.SmoothStep(0f, 1f, raw) * reachAmount;

            Quaternion restWorld = grabArm.parent != null
                ? grabArm.parent.rotation * _restLocalRotation
                : _restLocalRotation;

            // Reaching/Holding/Returning (pickup) aim at _target's own live position. Releasing aims at
            // _armAimPoint instead - for Place that's the ground spot (object is still at the hold
            // anchor, not there yet); for Throw it's just a forward gesture point (the object never
            // visually travels there - see ThrowTarget). ReleaseReturning aims at _target again for
            // Place (which now IS _placePosition, since PlaceTarget already moved it there) but NOT for
            // Throw - by then the object is physically flying off on its own, so the arm just eases
            // straight back to rest instead of trying to track it.
            Vector3? aimPoint;
            if (_phase == Phase.Releasing)
                aimPoint = _armAimPoint;
            else if (_phase == Phase.ReleaseReturning && ReleasingAsThrow)
                aimPoint = null;
            else
                aimPoint = _target != null ? _target.transform.position : (Vector3?)null;

            Quaternion aimWorld = restWorld;
            if (aimPoint.HasValue)
            {
                Vector3 dir = aimPoint.Value - grabArm.position;
                if (dir.sqrMagnitude > 1e-6f)
                {
                    // The arm capsule hangs along the pivot's local -Y; map that onto dir.
                    aimWorld = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(-90f, 0f, 0f);
                }
            }

            grabArm.rotation = Quaternion.Slerp(restWorld, aimWorld, t);
        }

        // Teardown only (component disabled / destroyed) - NOT a deliberate Place or Throw. Leaves the
        // object as a free dynamic body wherever it currently is so nothing stays stuck kinematic +
        // parented. Safe at any phase: mid-Reaching/mid-Releasing it just un-freezes an already-
        // un-parented or still-held object; after PlaceTarget()/ThrowTarget() has already run
        // (ReleaseReturning) everything it touches is already in its rest state, so this is a harmless
        // no-op on top of that (re-setting a velocity Throw just gave it would be the one exception,
        // but OnDisable never touches velocity - only isKinematic/parent/collider/Holder).
        private void OnDisable()
        {
            if (_target != null)
            {
                // Only the carried object was actually re-parented / frozen; for an in-flight grab
                // these are no-ops, and Release only clears the claim if it is ours.
                _target.transform.SetParent(null, worldPositionStays: true);
                if (_targetColliders != null)
                    foreach (var col in _targetColliders)
                        if (col != null) col.enabled = true;
                if (_target.Body != null)
                    _target.Body.isKinematic = false;
                _target.Release(this);
            }
            _targetColliders = null;
            _target = null;
            _dwellTarget = null;
            _phase = Phase.Idle;
            _phaseTimer = 0f;
            _dwellAccum = 0f;
            _carryTimer = 0f;
            _aborted = false;
            _probeCarry = false;
            _probeThrowPending = false;
            _probeThrowPreserveVertical = false;
        }

        private float FlatDistance(Vector3 worldPos)
        {
            Vector3 flat = worldPos - transform.position;
            flat.y = 0f;
            return flat.magnitude;
        }

        // Mirrors PlayerInteractor.ComputePivotToBottom: how far the object's transform sits above its
        // own lowest collider point, so Place can rest it flush on the ground instead of at the
        // pivot's own height.
        private static float ComputePivotToBottom(Transform t, Collider[] colliders)
        {
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (var col in colliders)
            {
                if (col == null) continue;
                if (!hasBounds) { bounds = col.bounds; hasBounds = true; }
                else bounds.Encapsulate(col.bounds);
            }
            return hasBounds ? t.position.y - bounds.min.y : 0f;
        }

        private void OnValidate()
        {
            pickupDelayMin = Mathf.Max(0f, pickupDelayMin);
            pickupDelayMax = Mathf.Max(pickupDelayMin, pickupDelayMax);
            grabRange = Mathf.Max(0f, grabRange);
            reachDuration = Mathf.Max(0f, reachDuration);
            holdDuration = Mathf.Max(0f, holdDuration);
            returnDuration = Mathf.Max(0f, returnDuration);
            carryDurationMin = Mathf.Max(0f, carryDurationMin);
            carryDurationMax = Mathf.Max(carryDurationMin, carryDurationMax);
            releaseReachDuration = Mathf.Max(0f, releaseReachDuration);
            releaseReturnDuration = Mathf.Max(0f, releaseReturnDuration);
            placeForwardDistance = Mathf.Max(0f, placeForwardDistance);
            placeRayStartHeight = Mathf.Max(0f, placeRayStartHeight);
            placeRayDistance = Mathf.Max(0.01f, placeRayDistance);
            throwForce = Mathf.Max(0f, throwForce);
            throwUpwardFraction = Mathf.Max(0f, throwUpwardFraction);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = _phase == Phase.Carrying ? new Color(0.4f, 1f, 0.6f) : new Color(0.4f, 1f, 0.6f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, grabRange);

            if (holdAnchor != null)
                Gizmos.DrawWireCube(holdAnchor.position, Vector3.one * 0.12f);

            if (Application.isPlaying && (_phase == Phase.Releasing || _phase == Phase.ReleaseReturning))
            {
                if (!ReleasingAsThrow)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireCube(_placePosition, Vector3.one * 0.15f);
                }
                else if (grabArm != null && _throwVelocity.sqrMagnitude > 1e-6f)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawRay(grabArm.position, _throwVelocity.normalized * 1.5f);
                }
            }
        }
    }
}
