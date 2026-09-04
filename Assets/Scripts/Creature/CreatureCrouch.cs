using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Crouch (0.1): a DEV-TEST-ONLY capability check, not a decision the creature makes or a reaction
    /// to the player. Press <see cref="testKey"/> in Play Mode to toggle between a Stand and a
    /// "low and wide" Crouch posture, easing smoothly over <see cref="transitionDuration"/>. Nothing in
    /// Attention / Movement / Probe / Pickup ever presses this key or reads <see cref="IsCrouching"/>.
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

        private Vector3 _bodyMeshStandScale;
        private Vector3 _bodyMeshStandLocalPos;
        private Vector3 _leftLegStandLocalPos;
        private Vector3 _rightLegStandLocalPos;
        private Vector3 _headStandLocalPos;
        private Vector3 _leftArmStandLocalPos;
        private Vector3 _rightArmStandLocalPos;
        private Vector3 _holdAnchorStandLocalPos;

        private bool _crouching;
        private float _t; // 0 = fully Stand, 1 = fully Crouch; eased and applied fresh every frame

        /// <summary>True once the toggle has requested Crouch (regardless of how far the transition has eased). Read-only seam; nothing in this prototype consumes it yet.</summary>
        public bool IsCrouching => _crouching;

        private void Awake()
        {
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
            var keyboard = Keyboard.current;
            if (testKey != Key.None && keyboard != null && keyboard[testKey].wasPressedThisFrame)
                _crouching = !_crouching;

            float target = _crouching ? 1f : 0f;
            _t = Mathf.MoveTowards(_t, target, Time.deltaTime / Mathf.Max(transitionDuration, 0.01f));

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
        }

        // Safety net only - not a gameplay path. If this component is disabled mid-transition, don't
        // leave the creature stuck in a squashed / spread posture.
        private void OnDisable()
        {
            _crouching = false;
            _t = 0f;
            ApplyPosture(0f);
        }

        private void OnValidate()
        {
            crouchBodyHeightScale = Mathf.Clamp(crouchBodyHeightScale, 0.05f, 1f);
            crouchBodyWidthMultiplier = Mathf.Max(0.1f, crouchBodyWidthMultiplier);
            legSpreadDelta = Mathf.Max(0f, legSpreadDelta);
            headLowerDelta = Mathf.Max(0f, headLowerDelta);
            armLowerDelta = Mathf.Max(0f, armLowerDelta);
            transitionDuration = Mathf.Max(0.01f, transitionDuration);
        }
    }
}
