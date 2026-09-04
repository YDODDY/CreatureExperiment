using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Jump (0.1): a DEV-TEST-ONLY capability check, not a decision the creature makes. Press
    /// <see cref="testKey"/> in Play Mode and the creature jumps exactly once from wherever it
    /// currently stands. This only verifies "the creature CAN execute a jump" - nothing in
    /// Attention / Movement / Probe / Pickup ever presses this key or reads <see cref="IsJumping"/>;
    /// there is no in-fiction reason for the jump, on purpose.
    ///
    /// Ownership (why this never fights CreatureMovement for transform.position): CreatureMovement
    /// already only ever touches the X/Z of the root's position - RetreatStep/ApproachStep add a
    /// delta with y left at 0, and InspectStep explicitly copies the existing Y straight through
    /// ("Y is never touched" per its own comment). So this component only ever writes the Y of that
    /// SAME transform.position, recomputed every frame from a simple kinematic velocity, and never
    /// touches X/Z. Whichever of the two Update()s happens to run first this frame, the other's
    /// later read-modify-write only ever changes its own axis, so they compose for free with no
    /// arbitration flag and no change to CreatureMovement at all. Approach / Retreat / Inspect keep
    /// running during a jump as a result - deliberately: pausing them would mean adding an
    /// "IsJumping" branch into CreatureMovement, which this stays independent of entirely.
    ///
    /// LookPivot and HeadPivot are children of this same root (siblings of BodyVisual, not
    /// descendants of it), so they - and therefore gaze / head-turn / pupil, which all work in world
    /// space off those pivots' live positions - rise and fall with the jump automatically through the
    /// transform hierarchy. Nothing in CreaturePerception needed to change for that to be true.
    ///
    /// Ground height is captured once in Awake as the starting Y (this room's floor is flat, so one
    /// value is enough - not a grounding/raycast framework). Deliberately tiny: no Rigidbody, no
    /// CharacterController, no gravity system, no double/air jump, no wall collision handling
    /// (already unresolved project-wide, untouched here).
    /// </summary>
    public class CreatureJump : MonoBehaviour
    {
        [Header("DEV TEST ONLY - manual capability trigger, not gameplay input")]
        [Tooltip("Press this key in Play Mode to trigger one jump. Purely a manual test device - change it here if it ever clashes with something else; it is not part of the shared Player Input Actions asset.")]
        [SerializeField] private Key testKey = Key.J;

        [Header("Jump")]
        [Tooltip("Peak height above the ground, in metres.")]
        [SerializeField] private float jumpHeight = 1f;
        [Tooltip("Downward acceleration for the rise/fall arc, in m/s^2 (positive number).")]
        [SerializeField] private float gravity = 19.62f;

        private float _groundY;
        private bool _jumping;
        private float _verticalVelocity;

        /// <summary>True while a jump is in flight. Read-only seam; nothing in this prototype consumes it yet.</summary>
        public bool IsJumping => _jumping;

        private void Awake()
        {
            _groundY = transform.position.y;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (!_jumping && testKey != Key.None && keyboard != null && keyboard[testKey].wasPressedThisFrame)
                StartJump();

            if (!_jumping)
                return;

            _verticalVelocity -= gravity * Time.deltaTime;

            Vector3 pos = transform.position;
            float newY = pos.y + _verticalVelocity * Time.deltaTime;

            if (newY <= _groundY)
            {
                // Landed - snap exactly to the original ground height rather than overshooting below it.
                newY = _groundY;
                _jumping = false;
                _verticalVelocity = 0f;
            }

            transform.position = new Vector3(pos.x, newY, pos.z);
        }

        private void StartJump()
        {
            _jumping = true;
            // v0 such that a constant-gravity arc peaks at exactly jumpHeight: v0 = sqrt(2*g*h).
            _verticalVelocity = Mathf.Sqrt(2f * gravity * Mathf.Max(jumpHeight, 0.01f));
        }

        // Safety net only - not a gameplay path. If this component is disabled mid-air, don't leave
        // the creature floating.
        private void OnDisable()
        {
            if (!_jumping)
                return;

            Vector3 pos = transform.position;
            transform.position = new Vector3(pos.x, _groundY, pos.z);
            _jumping = false;
            _verticalVelocity = 0f;
        }

        private void OnValidate()
        {
            jumpHeight = Mathf.Max(0f, jumpHeight);
            gravity = Mathf.Max(0.01f, gravity);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            float baseY = Application.isPlaying ? _groundY : transform.position.y;
            Gizmos.DrawWireSphere(new Vector3(transform.position.x, baseY + jumpHeight, transform.position.z), 0.12f);
        }
    }
}
