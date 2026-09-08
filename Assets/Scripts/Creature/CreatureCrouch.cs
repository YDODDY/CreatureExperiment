using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Crouch (0.1): a "low and wide" posture that eases smoothly over <see cref="transitionDuration"/>.
    /// Three ways in, one implementation and one safety gate:
    /// <list type="bullet">
    /// <item>DEV: press <see cref="testKey"/> in Play Mode - a pure capability check.</item>
    /// <item>Expressive: <see cref="CreaturePlayerObserve"/> calls <see cref="SetCrouching"/> to hunker
    /// down briefly while it watches the player.</item>
    /// <item>Environmental (0.1): this component itself, each frame, looks a short distance along the
    /// creature's ACTUAL current movement (frame-to-frame XZ delta - it never creates a destination,
    /// only reacts to motion some other behaviour is already producing) for a ceiling low enough that
    /// the standing capsule would not fit but a crouched one would, and requests Crouch. It also holds
    /// Crouch whenever a stand right now would have no headroom.</item>
    /// </list>
    /// The dev key and <see cref="SetCrouching"/> both set <c>_wantCrouchExternal</c>; the effective
    /// target is <c>_wantCrouchExternal || environmentRequiresCrouch || !HasStandClearance()</c>. That
    /// last term is the whole point of Environmental Crouch 0.1: a Stand request (dev key, or Player
    /// Observe's <see cref="SetCrouching"/>(false)) made under a low ceiling is DEFERRED, not obeyed -
    /// the creature stays down until it is physically clear to rise. No requester registry, no
    /// priority stack; just one OR and one clearance test the capability owns.
    ///
    /// This is a REAL physical crouch, not just visuals: the same eased <c>_t</c> also lerps the
    /// creature's <see cref="CapsuleCollider"/> down - <see cref="crouchColliderHeight"/> with the
    /// centre re-anchored so the feet stay planted and the top comes down - so the crouched creature
    /// actually fits through the low tunnel. <see cref="CreatureWallCollision"/> re-reads the collider
    /// live every frame, so it depenetrates whatever size the capsule currently is with no change
    /// needed there. Nothing in Attention / Movement / Probe / Pickup touches this component.
    ///
    /// The creature is a plain capsule mesh + primitive limb pivots, not a rig with knees, so this is
    /// not a squat animation - it is "fold low and wide": <see cref="bodyMesh"/> (the capsule under
    /// BodyVisual) is squashed on Y and widened on X/Z, re-anchored each frame so its bottom stays put
    /// while the top comes down (reads as lowering onto the ground, not shrinking in place); the leg
    /// pivots move outward for a wider stance; <see cref="headPivot"/> drops a little with the body.
    /// Deliberately NOT touched: creature root position/Y (owned by CreatureMovement/CreatureJump),
    /// BodyVisual's own transform (its rotation is CreatureBodyExpression's; scaling it would also
    /// scale limb pivot offsets - out of scope), and the arms (see below).
    ///
    /// Transform ownership - why only these three, and only these properties of them:
    /// <list type="bullet">
    /// <item><see cref="bodyMesh"/>: nothing else in the project ever reads or writes its transform.
    /// Fully free to use for both scale and position.</item>
    /// <item><see cref="leftLeg"/> / <see cref="rightLeg"/>: <see cref="CreatureBodyExpression"/>
    /// writes their <c>localRotation</c> every single frame (walk swing / rest). This component only
    /// ever writes their <c>localPosition</c> (spread outward), which CreatureBodyExpression never
    /// touches - so the two never fight, and leg swing keeps animating on top of the crouch stance
    /// while walking.</item>
    /// <item><see cref="headPivot"/>: <see cref="CreaturePerception"/> writes its <c>rotation</c> every
    /// frame for gaze tracking, and that math never reads the pivot's position (it works off
    /// <c>lookPivot.forward</c> and <c>Quaternion.Euler</c> aim angles alone). So lowering only
    /// <c>localPosition</c> here cannot disturb tracking, and CreaturePerception never needed to
    /// change.</item>
    /// </list>
    /// <c>LeftArm</c>/<c>RightArm</c> (added 2026-09-04, after the compressed torso started reading as
    /// leaving them floating beside it): only <c>localPosition.y</c> is lowered, exactly like
    /// <see cref="headPivot"/> above and for the identical reason - <c>localRotation</c> on both arms
    /// is already contested between <see cref="CreatureBodyExpression"/> (every frame, walk swing) and
    /// the brief <c>LateUpdate</c> overrides from <see cref="CreaturePhysicalProbe"/> (RightArm) and
    /// <see cref="CreaturePickup"/> (LeftArm), so rotation is never touched here. Position was
    /// completely unowned by any of them, so lowering it fights nothing: walk-swing rotation keeps
    /// animating on top of the lowered pivot exactly as leg rotation already does over the leg spread,
    /// and Probe/Pickup's aim math reads <c>grabArm.position</c>/<c>probeArm.position</c> live every
    /// frame, so it automatically reaches from wherever Crouch currently has the pivot - no code there
    /// needed to change.
    ///
    /// <c>holdAnchor</c> (<c>CreatureHoldAnchor</c>, added 2026-09-04): a sibling of the arms under
    /// BodyVisual, not parented to LeftArm, so it did not follow when the arms above got lowered -
    /// while carrying, a held object (parented to it by <see cref="CreaturePickup"/>) stayed at Stand
    /// height regardless of Crouch. Fixed by dropping its <c>localPosition.y</c> too, reusing the exact
    /// same <c>armLowerDelta</c>-driven amount already computed for the arms in the same
    /// <see cref="ApplyPosture"/> call (not a separately tuned value) - checked the actual geometry
    /// first: <c>holdAnchor.y - leftArm.y</c> is a constant -0.60 at Stand, and moving both by the
    /// identical delta at every point of <c>_t</c> keeps that gap exactly constant through the whole
    /// transition, i.e. the anchor rides down/up in a perfect rigid lock-step with the arm rather than
    /// drifting relative to it mid-transition. <see cref="CreaturePickup"/> itself is untouched and
    /// needed no changes: it already just parents the held object under <c>holdAnchor</c> and never
    /// re-reads the anchor's position after that, so the object's own transform is never written here
    /// either - it rides along purely through the existing parent-child relationship.
    /// </summary>
    [RequireComponent(typeof(CapsuleCollider))]
    [DefaultExecutionOrder(60)] // after the movers (CreatureMovement 0, PlayerObserve 40, Wander 50) so the frame's real movement delta is known; before CreatureWallCollision (200)
    public class CreatureCrouch : MonoBehaviour
    {
        [Header("References (posture-only transforms - see class doc for why these and not others)")]
        [Tooltip("BodyMesh - the capsule mesh under BodyVisual. Squashed on Y and widened on X/Z for crouch. Nothing else in the project touches its transform.")]
        [SerializeField] private Transform bodyMesh;
        [Tooltip("LeftLeg pivot. Only its localPosition is moved (spread outward) - CreatureBodyExpression drives its localRotation every frame, so rotation is left alone entirely.")]
        [SerializeField] private Transform leftLeg;
        [Tooltip("RightLeg pivot. Same treatment as leftLeg, mirrored.")]
        [SerializeField] private Transform rightLeg;
        [Tooltip("HeadPivot (child of the creature root, sibling of BodyVisual). Only its localPosition is lowered - CreaturePerception drives its rotation every frame for gaze tracking, so rotation is left alone entirely.")]
        [SerializeField] private Transform headPivot;
        [Tooltip("LeftArm pivot (child of BodyVisual). Only its localPosition.y is lowered - CreatureBodyExpression/CreaturePickup drive its rotation, so rotation is left alone entirely.")]
        [SerializeField] private Transform leftArm;
        [Tooltip("RightArm pivot (child of BodyVisual). Only its localPosition.y is lowered - CreatureBodyExpression/CreaturePhysicalProbe drive its rotation, so rotation is left alone entirely.")]
        [SerializeField] private Transform rightArm;
        [Tooltip("CreatureHoldAnchor (child of BodyVisual, sibling of the arms) - CreaturePickup parents a held object to this. Its localPosition.y is dropped by the SAME amount as the arms (armLowerDelta) each frame, so a carried object stays rigidly attached to the lowered arm instead of hanging back at Stand height. Not required to hold anything - fine to move even while empty.")]
        [SerializeField] private Transform holdAnchor;

        [Header("DEV TEST ONLY - manual capability trigger, not gameplay input")]
        [Tooltip("Press this key in Play Mode to toggle Stand <-> Crouch. Purely a manual test device - change it here if it ever clashes with something else; it is not part of the shared Player Input Actions asset.")]
        [SerializeField] private Key testKey = Key.C;

        [Header("Posture")]
        [Tooltip("BodyMesh's Y scale at full crouch. Its stand scale is read live from the transform at Awake, not assumed.")]
        [SerializeField] private float crouchBodyHeightScale = 0.6f;
        [Tooltip("Multiplier on BodyMesh's stand X/Z scale at full crouch, for a wider stance.")]
        [SerializeField] private float crouchBodyWidthMultiplier = 1.15f;
        [Tooltip("How far each leg pivot moves outward (+/-X) at full crouch, in metres.")]
        [SerializeField] private float legSpreadDelta = 0.1f;
        [Tooltip("How far HeadPivot's local Y drops at full crouch, in metres. Sized so the Head sphere's bottom lands just above BodyMesh's crouched top (see class doc) rather than floating high above it or burying into it.")]
        [SerializeField] private float headLowerDelta = 0.32f;
        [Tooltip("How far each arm pivot's local Y drops at full crouch, in metres. Sized so the shoulder lands just inside BodyMesh's crouched top (see class doc) instead of hovering above the compressed torso.")]
        [SerializeField] private float armLowerDelta = 0.32f;
        [Tooltip("Seconds for a full Stand<->Crouch transition. Toggling mid-transition just reverses smoothly from wherever it currently is.")]
        [SerializeField] private float transitionDuration = 0.35f;

        [Header("Environmental crouch (0.1) - real collider + clearance, no navigation")]
        [Tooltip("The CapsuleCollider's HEIGHT at full crouch (its standing height and centre are read live at Awake). Feet stay planted; the top comes down. Keep this below the lowest ceiling you want the creature to pass under - the sandbox crouch tunnel gives ~1.30 m of clearance.")]
        [SerializeField] private float crouchColliderHeight = 1.1f;
        [Tooltip("How far ahead along the creature's actual movement direction to look for a low ceiling, in metres. Big enough that the transition finishes before the creature reaches it.")]
        [SerializeField] private float lookAheadDistance = 1f;
        [Tooltip("Probe spheres are shrunk by this much (metres) so they do not catch geometry the creature is merely brushing past.")]
        [SerializeField] private float clearanceSkin = 0.05f;
        [Tooltip("Flat speed (m/s) above which the creature counts as 'moving' for the forward low-ceiling probe. Below it, only the overhead safety check runs.")]
        [SerializeField] private float moveSpeedThreshold = 0.05f;
        [Tooltip("Layers the clearance probes treat as blocking (ceilings, walls, props). Default Everything; the floor and the creature's own collider are excluded by geometry / a self-filter, so Everything is fine.")]
        [SerializeField] private LayerMask clearanceMask = ~0;

        [Header("Environmental crouch state (read-only, for debugging)")]
        [Tooltip("True when a stand right here, right now would have no headroom - Stand requests are refused while this holds.")]
        [SerializeField] private bool standBlocked;
        [Tooltip("True when a low ceiling is close ahead along the current movement direction.")]
        [SerializeField] private bool environmentRequiresCrouch;
        [Tooltip("The effective crouch target this frame: external request OR environment ahead OR no headroom to rise.")]
        [SerializeField] private bool effectiveCrouch;

        private Vector3 _bodyMeshStandScale;
        private Vector3 _bodyMeshStandLocalPos;
        private Vector3 _leftLegStandLocalPos;
        private Vector3 _rightLegStandLocalPos;
        private Vector3 _headStandLocalPos;
        private Vector3 _leftArmStandLocalPos;
        private Vector3 _rightArmStandLocalPos;
        private Vector3 _holdAnchorStandLocalPos;

        private CapsuleCollider _capsule;
        private float _standColliderHeight;
        private float _standColliderCenterY;
        private Vector3 _lastPos;

        private bool _wantCrouchExternal; // the dev key / SetCrouching request - NOT the effective state
        private bool _crouching;          // effective target this frame (external OR environment OR no-headroom)
        private float _t; // 0 = fully Stand, 1 = fully Crouch; eased and applied fresh every frame

        private readonly Collider[] _probeHits = new Collider[8];

        /// <summary>True while the effective posture target is Crouch - whether that came from a request, the environment ahead, or being under a ceiling with no room to rise. Reflects the eased posture's destination, not how far it has got.</summary>
        public bool IsCrouching => _crouching;

        /// <summary>True when a stand right now would have no headroom. While this holds, <see cref="SetCrouching"/>(false) and the dev key cannot bring the creature up.</summary>
        public bool StandBlocked => standBlocked;

        /// <summary>
        /// Request seam: <paramref name="on"/> true asks for Crouch, false asks for Stand. This only
        /// sets the EXTERNAL request - the effective posture is still
        /// <c>request || environmentRequiresCrouch || !HasStandClearance()</c>, so a Stand asked for
        /// under a low ceiling is deferred until the creature is physically clear. Same field the dev
        /// <see cref="testKey"/> toggles; used by <see cref="CreaturePlayerObserve"/>. Idempotent.
        /// </summary>
        public void SetCrouching(bool on) => _wantCrouchExternal = on;

        private void Awake()
        {
            _capsule = GetComponent<CapsuleCollider>();
            if (_capsule != null)
            {
                _standColliderHeight = _capsule.height;
                _standColliderCenterY = _capsule.center.y;
            }
            _lastPos = transform.position;

            if (bodyMesh != null)
            {
                _bodyMeshStandScale = bodyMesh.localScale;
                _bodyMeshStandLocalPos = bodyMesh.localPosition;
            }
            if (leftLeg != null) _leftLegStandLocalPos = leftLeg.localPosition;
            if (rightLeg != null) _rightLegStandLocalPos = rightLeg.localPosition;
            if (headPivot != null) _headStandLocalPos = headPivot.localPosition;
            if (leftArm != null) _leftArmStandLocalPos = leftArm.localPosition;
            if (rightArm != null) _rightArmStandLocalPos = rightArm.localPosition;
            if (holdAnchor != null) _holdAnchorStandLocalPos = holdAnchor.localPosition;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // DEV manual test now toggles the EXTERNAL request, exactly like SetCrouching - never the
            // effective posture directly.
            var keyboard = Keyboard.current;
            if (testKey != Key.None && keyboard != null && keyboard[testKey].wasPressedThisFrame)
                _wantCrouchExternal = !_wantCrouchExternal;

            // The creature's ACTUAL movement this frame (flat). Environmental crouch only ever reacts
            // to motion another behaviour is already producing - it never sets a destination.
            Vector3 delta = transform.position - _lastPos;
            delta.y = 0f;
            _lastPos = transform.position;
            float speed = dt > 0f ? delta.magnitude / dt : 0f;
            Vector3 moveDir = (speed > moveSpeedThreshold && delta.sqrMagnitude > 1e-8f)
                ? delta.normalized
                : Vector3.zero;

            environmentRequiresCrouch = moveDir != Vector3.zero && ForwardPathRequiresCrouch(moveDir);
            standBlocked = !HasStandClearance();

            // Effective target. The !HasStandClearance() term is the safety gate: a Stand request made
            // under a low ceiling is deferred, not obeyed, until there is room to rise.
            effectiveCrouch = _wantCrouchExternal || environmentRequiresCrouch || standBlocked;
            _crouching = effectiveCrouch;

            float target = _crouching ? 1f : 0f;
            _t = Mathf.MoveTowards(_t, target, dt / Mathf.Max(transitionDuration, 0.01f));

            // Every frame recomputes the full posture as a pure function of _t against the cached
            // stand baselines above - never an incremental nudge - so repeated toggling can never
            // drift away from the original Stand transforms.
            ApplyPosture(Mathf.SmoothStep(0f, 1f, _t));
        }

        private void ApplyPosture(float e)
        {
            if (bodyMesh != null)
            {
                float scaleY = Mathf.Lerp(_bodyMeshStandScale.y, crouchBodyHeightScale, e);
                float scaleX = Mathf.Lerp(_bodyMeshStandScale.x, _bodyMeshStandScale.x * crouchBodyWidthMultiplier, e);
                float scaleZ = Mathf.Lerp(_bodyMeshStandScale.z, _bodyMeshStandScale.z * crouchBodyWidthMultiplier, e);
                bodyMesh.localScale = new Vector3(scaleX, scaleY, scaleZ);

                // Re-anchor so the mesh's bottom stays exactly where it was at Stand while the top
                // compresses down toward it - reads as the body lowering onto the ground rather than
                // shrinking in place / floating. Unity's capsule primitive is centred on its own
                // transform, so bottom = localY - scaleY; holding that constant as scaleY changes:
                // newLocalY = standLocalY + (scaleY - standScaleY).
                float newLocalY = _bodyMeshStandLocalPos.y + (scaleY - _bodyMeshStandScale.y);
                bodyMesh.localPosition = new Vector3(_bodyMeshStandLocalPos.x, newLocalY, _bodyMeshStandLocalPos.z);
            }

            float spread = Mathf.Lerp(0f, legSpreadDelta, e);
            if (leftLeg != null)
                leftLeg.localPosition = _leftLegStandLocalPos + new Vector3(-spread, 0f, 0f);
            if (rightLeg != null)
                rightLeg.localPosition = _rightLegStandLocalPos + new Vector3(spread, 0f, 0f);

            if (headPivot != null)
            {
                float drop = Mathf.Lerp(0f, headLowerDelta, e);
                headPivot.localPosition = _headStandLocalPos - new Vector3(0f, drop, 0f);
            }

            // Position only - rotation on both arms belongs to CreatureBodyExpression (walk swing) and,
            // briefly, CreaturePhysicalProbe / CreaturePickup. Never touched here; see class doc.
            float armDrop = Mathf.Lerp(0f, armLowerDelta, e);
            if (leftArm != null)
                leftArm.localPosition = _leftArmStandLocalPos - new Vector3(0f, armDrop, 0f);
            if (rightArm != null)
                rightArm.localPosition = _rightArmStandLocalPos - new Vector3(0f, armDrop, 0f);

            // holdAnchor reuses the SAME armDrop value computed above (not its own delta) so it can
            // never drift relative to the arm it hangs from, at any point mid-transition. A held
            // object rides along for free through the existing parent-child relationship
            // CreaturePickup already set up - its own transform is never touched here.
            if (holdAnchor != null)
                holdAnchor.localPosition = _holdAnchorStandLocalPos - new Vector3(0f, armDrop, 0f);

            // Real physical crouch: lerp the CapsuleCollider down on the SAME eased _t. Height shrinks
            // toward crouchColliderHeight; centre.y drops by exactly half the height loss so the
            // capsule's BOTTOM (the feet) stays fixed and only the top comes down - identical idea to
            // the BodyMesh re-anchor above. Radius and direction are left alone. CreatureWallCollision
            // reads this live, so it always depenetrates the current size with no change needed there.
            if (_capsule != null)
            {
                float h = Mathf.Lerp(_standColliderHeight, crouchColliderHeight, e);
                Vector3 c = _capsule.center;
                c.y = Mathf.Lerp(_standColliderCenterY, _standColliderCenterY - (_standColliderHeight - crouchColliderHeight) * 0.5f, e);
                _capsule.center = c;
                _capsule.height = h;
            }
        }

        // Is there a low ceiling close ahead along the creature's current movement? True only when the
        // STANDING capsule's head would be blocked at the look-ahead point but a CROUCHED capsule
        // would still fit there (otherwise it is a wall, which CreatureWallCollision already handles).
        private bool ForwardPathRequiresCrouch(Vector3 moveDir)
        {
            Vector3 ahead = transform.position + moveDir * lookAheadDistance;
            float footY = FootWorldY();
            float r = Mathf.Max(0.01f, _capsule.radius - clearanceSkin);

            bool standHeadBlockedAhead = AnyObstacleAt(ahead, footY + _standColliderHeight - _capsule.radius, r);
            if (!standHeadBlockedAhead)
                return false;

            // Would a crouched creature actually pass there? Probe at the crouched capsule's own head
            // height; if that is clear, it is a passage, not a wall.
            bool crouchFitsAhead = !AnyObstacleAt(ahead, footY + crouchColliderHeight - _capsule.radius, r);
            return crouchFitsAhead;
        }

        // Could the creature stand right here, right now, without its head hitting anything? Mirrors
        // PlayerMovement.CanStandUp: one sphere where the standing capsule's top sphere-centre would
        // be. Self-filtered so the creature's own (currently crouched) capsule never counts.
        private bool HasStandClearance()
        {
            if (_capsule == null)
                return true;
            float footY = FootWorldY();
            float r = Mathf.Max(0.01f, _capsule.radius - clearanceSkin);
            return !AnyObstacleAt(transform.position, footY + _standColliderHeight - _capsule.radius, r);
        }

        // World Y of the capsule's bottom (the feet). Derived from the STANDING size cached at Awake
        // and the live root Y, so it tracks the root (e.g. a dev-test Jump) without drifting as the
        // collider is resized.
        private float FootWorldY()
        {
            return transform.position.y + _standColliderCenterY - _standColliderHeight * 0.5f;
        }

        // True if anything on clearanceMask (other than the creature itself) overlaps a sphere of
        // <paramref name="radius"/> centred at (xz of <paramref name="atXZ"/>, <paramref name="worldY"/>).
        private bool AnyObstacleAt(Vector3 atXZ, float worldY, float radius)
        {
            Vector3 centre = new Vector3(atXZ.x, worldY, atXZ.z);
            int n = Physics.OverlapSphereNonAlloc(centre, radius, _probeHits, clearanceMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider h = _probeHits[i];
                if (h == null || h == _capsule)
                    continue;
                if (h.transform == transform || h.transform.IsChildOf(transform))
                    continue;
                return true;
            }
            return false;
        }

        // Safety net only - not a gameplay path. If this component is disabled mid-transition, don't
        // leave the creature stuck in a squashed / spread posture or a shrunken collider.
        private void OnDisable()
        {
            _wantCrouchExternal = false;
            _crouching = false;
            _t = 0f;
            ApplyPosture(0f); // also restores the standing collider height / centre
        }

        private void OnValidate()
        {
            crouchBodyHeightScale = Mathf.Clamp(crouchBodyHeightScale, 0.05f, 1f);
            crouchBodyWidthMultiplier = Mathf.Max(0.1f, crouchBodyWidthMultiplier);
            legSpreadDelta = Mathf.Max(0f, legSpreadDelta);
            headLowerDelta = Mathf.Max(0f, headLowerDelta);
            armLowerDelta = Mathf.Max(0f, armLowerDelta);
            transitionDuration = Mathf.Max(0.01f, transitionDuration);
            crouchColliderHeight = Mathf.Max(0.2f, crouchColliderHeight);
            lookAheadDistance = Mathf.Max(0f, lookAheadDistance);
            clearanceSkin = Mathf.Max(0f, clearanceSkin);
            moveSpeedThreshold = Mathf.Max(0f, moveSpeedThreshold);
        }

        private void OnDrawGizmosSelected()
        {
            var capsule = _capsule != null ? _capsule : GetComponent<CapsuleCollider>();
            if (capsule == null)
                return;

            float standH = Application.isPlaying ? _standColliderHeight : capsule.height;
            float standCY = Application.isPlaying ? _standColliderCenterY : capsule.center.y;
            float footY = transform.position.y + standCY - standH * 0.5f;
            float r = Mathf.Max(0.01f, capsule.radius - clearanceSkin);

            // overhead stand-clearance sphere (green = clear / red = blocked)
            Vector3 head = new Vector3(transform.position.x, footY + standH - capsule.radius, transform.position.z);
            Gizmos.color = (Application.isPlaying && standBlocked) ? new Color(1f, 0.3f, 0.2f) : new Color(0.3f, 1f, 0.4f);
            Gizmos.DrawWireSphere(head, r);

            // forward look-ahead probe point at standing-head height
            Vector3 fwd = transform.forward;
            if (Application.isPlaying)
            {
                Vector3 d = transform.position - _lastPos; d.y = 0f;
                if (d.sqrMagnitude > 1e-8f) fwd = d.normalized;
            }
            Vector3 ahead = transform.position + fwd.normalized * lookAheadDistance;
            Gizmos.color = (Application.isPlaying && environmentRequiresCrouch) ? new Color(1f, 0.6f, 0.1f) : new Color(0.5f, 0.7f, 1f);
            Gizmos.DrawLine(head, new Vector3(ahead.x, head.y, ahead.z));
            Gizmos.DrawWireSphere(new Vector3(ahead.x, footY + standH - capsule.radius, ahead.z), r);
        }
    }
}
