using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Dash (0.1): a flat speed substitution for <see cref="dashDuration"/> seconds. Two ways in, one
    /// implementation: press <see cref="testKey"/> for a pure DEV-TEST capability check, OR
    /// <see cref="CreatureApproachDash"/> calls <see cref="RequestDash"/> for one burst partway through
    /// a long Object Approach (that component owns the WHEN; this still owns only the HOW-FAST). Either
    /// way, a dash already running is never re-triggered, reset or extended - dashes never stack or
    /// chain. Still no Observation/Memory/Intent/Decision wiring in here.
    ///
    /// Ownership split (why this never fights CreatureMovement for transform.position): CreatureMovement
    /// decides WHERE to move - Retreat away from the player, Approach <c>_activeTarget</c>, or slide
    /// around it while Inspecting. This component only decides HOW FAST whichever of those is currently
    /// running is allowed to go: CreatureMovement asks <see cref="IsDashing"/> / <see cref="DashSpeed"/>
    /// and, while dashing, substitutes <see cref="DashSpeed"/> for its own retreatSpeed / approachSpeed /
    /// inspectMoveSpeed at the point where it already multiplies a direction vector by a speed - the
    /// direction itself is untouched, so Dash never creates a new heading and never reads Head / Gaze /
    /// BodyVisual.forward. If CreatureMovement has no CreatureDash sibling, it behaves exactly as before.
    ///
    /// <see cref="dashSpeed"/> is a flat metres/second value, not a multiplier on top of
    /// CreatureMovement's own speeds, so it stays directly comparable to <c>PlayerMovement.sprintSpeed</c>
    /// (7 m/s) and stays meaningful however slow/fast the creature's normal speeds are tuned later.
    /// </summary>
    public class CreatureDash : MonoBehaviour
    {
        [Header("DEV TEST ONLY - manual capability trigger, not gameplay input")]
        [Tooltip("Press (do not hold) this key in Play Mode to trigger one dash. Purely a manual test device; not part of the shared Player Input Actions asset.")]
        [SerializeField] private Key testKey = Key.LeftShift;

        [Header("Dash")]
        [Tooltip("How long a single dash lasts once triggered, in seconds.")]
        [SerializeField] private float dashDuration = 0.35f;
        [Tooltip("Speed CreatureMovement uses in place of its own verb speed while dashing, in metres/second. Wide range on purpose: set well below the creature's normal speed for a sluggish dash, or far above for an inhuman burst. Default is calibrated to feel similar to the player's Sprint speed (~7 m/s).")]
        [SerializeField] private float dashSpeed = 7f;

        private float _timer;

        /// <summary>True while a dash is in progress. CreatureMovement reads this to decide whether to substitute DashSpeed for its own verb speed.</summary>
        public bool IsDashing { get; private set; }

        /// <summary>The flat speed (m/s) to use in place of the current movement verb's own speed while <see cref="IsDashing"/> is true.</summary>
        public float DashSpeed => dashSpeed;

        /// <summary>
        /// Autonomy seam: start one dash now, exactly as the dev key does - same <see cref="dashDuration"/>
        /// and <see cref="dashSpeed"/>, no duplicated timer. No-op while a dash is already running, so it
        /// can never stack or extend. Used by <see cref="CreatureApproachDash"/>.
        /// </summary>
        public void RequestDash()
        {
            if (IsDashing)
                return;
            IsDashing = true;
            _timer = dashDuration;
        }

        /// <summary>
        /// Autonomy seam: end the current dash immediately - for when the behaviour that asked for it
        /// (a long Object Approach) was cancelled or handed off. Safe any time; a no-op if not dashing.
        /// Does not affect a future dev-key dash.
        /// </summary>
        public void CancelDash()
        {
            IsDashing = false;
            _timer = 0f;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (!IsDashing && testKey != Key.None && keyboard != null && keyboard[testKey].wasPressedThisFrame)
                RequestDash();

            if (!IsDashing)
                return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
                IsDashing = false;
        }

        private void OnValidate()
        {
            dashDuration = Mathf.Max(0.01f, dashDuration);
            dashSpeed = Mathf.Max(0f, dashSpeed);
        }
    }
}
