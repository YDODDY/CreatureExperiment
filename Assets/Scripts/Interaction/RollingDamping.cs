using UnityEngine;

namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// Rolling resistance for round props (cans, bottles). PhysX has no rolling friction: a capsule / cylinder that lands
    /// rolling keeps almost all of its spin (the Rigidbody's default angular damping is 0.05), so a thrown can rolls
    /// on and on. This adds resistance only while the body touches something - in the air a throw flies and spins
    /// exactly as before - so it goes "clunk, a few rolls, slows, stops". Other items are untouched: it is opt-in per object.
    ///
    /// Each physics step in contact: spin and horizontal speed decay by <see cref="contactAngularDamping"/> /
    /// <see cref="contactLinearDamping"/> (per second, exponential-like); once it is nearly still it is settled faster
    /// (<see cref="settleSpeed"/>). Spin is capped at <see cref="maxAngularSpeed"/> rad/s. Vertical speed is never touched.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RollingDamping : MonoBehaviour
    {
        [Tooltip("Spin decay per second while touching a surface.")]
        [SerializeField] private float contactAngularDamping = 3f;
        [Tooltip("Horizontal speed decay per second while touching a surface.")]
        [SerializeField] private float contactLinearDamping = 0.8f;
        [Tooltip("Spin cap (rad/s) - also stops a fast throw from turning into a spinning top.")]
        [SerializeField] private float maxAngularSpeed = 25f;
        [Tooltip("Below this speed (m/s) in contact, the body is brought to rest quickly.")]
        [SerializeField] private float settleSpeed = 0.25f;
        [Tooltip("Extra decay per second used while settling.")]
        [SerializeField] private float settleDamping = 8f;

        private Rigidbody _body;
        private bool _touching;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.maxAngularVelocity = maxAngularSpeed;
        }

        // Collision callbacks come after the physics step; FixedUpdate (before the next step) uses them, then clears.
        private void OnCollisionEnter(Collision collision) => _touching = true;
        private void OnCollisionStay(Collision collision) => _touching = true;

        private void FixedUpdate()
        {
            bool touching = _touching;
            _touching = false;
            if (!touching || _body.isKinematic)
                return;

            float dt = Time.fixedDeltaTime;
            Vector3 v = _body.linearVelocity;
            var horizontal = new Vector3(v.x, 0f, v.z);
            bool settling = horizontal.magnitude < settleSpeed;

            float angularKeep = 1f / (1f + (contactAngularDamping + (settling ? settleDamping : 0f)) * dt);
            float linearKeep = 1f / (1f + (contactLinearDamping + (settling ? settleDamping : 0f)) * dt);

            _body.angularVelocity *= angularKeep;
            horizontal *= linearKeep;
            _body.linearVelocity = new Vector3(horizontal.x, v.y, horizontal.z);
        }
    }
}
