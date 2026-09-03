using UnityEngine;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Visual only: swings four primitive limb pivots back and forth while the creature root is
    /// actually translating, so movement reads as "walking" rather than a blob sliding.
    ///
    /// It does not know or care why the creature is moving. Each frame it measures the root's flat
    /// (XZ) frame-to-frame displacement; above <see cref="moveSpeedThreshold"/> the limbs engage,
    /// below it they relax back to the rest pose (pivot local rotation = identity). Left arm swings
    /// with the right leg, right arm with the left leg (opposite phase).
    ///
    /// Body orientation: while moving, <see cref="bodyVisual"/> (the parent of the mesh + limbs, but
    /// NOT of the look / head pivots) is yawed toward that same travel direction at
    /// <see cref="bodyTurnSpeed"/>, so the walk faces where it is going instead of moonwalking. It is
    /// never forced while standing still. The head keeps aiming wherever CreaturePerception points it
    /// because the look / head pivots stay outside bodyVisual.
    ///
    /// Not an animation system: no Animator, no clips, no blend tree, no IK, no bones. Just a sine
    /// wave and a LookRotation. <see cref="CreatureMovement"/> and <see cref="CreaturePerception"/>
    /// are never read and never touched.
    /// </summary>
    public class CreatureBodyExpression : MonoBehaviour
    {
        [Header("Limb pivots (empty transforms at the shoulders / hips)")]
        [SerializeField] private Transform leftArm;
        [SerializeField] private Transform rightArm;
        [SerializeField] private Transform leftLeg;
        [SerializeField] private Transform rightLeg;

        [Header("Swing")]
        [Tooltip("Peak forward/back swing angle, in degrees.")]
        [SerializeField] private float swingAmplitude = 32f;
        [Tooltip("Sine phase advance while moving, in radians per second (cadence).")]
        [SerializeField] private float swingSpeed = 8f;
        [Tooltip("Flat speed (m/s) above which the limbs count as 'moving'.")]
        [SerializeField] private float moveSpeedThreshold = 0.05f;
        [Tooltip("How quickly the limbs engage when moving starts / relax when it stops.")]
        [SerializeField] private float blendSharpness = 8f;

        [Header("Body orientation")]
        [Tooltip("Parent of the mesh + limbs (not the look/head pivots). Yawed toward the travel direction while moving.")]
        [SerializeField] private Transform bodyVisual;
        [Tooltip("How fast the body turns to face the movement direction, in degrees per second.")]
        [SerializeField] private float bodyTurnSpeed = 270f;

        private Vector3 _lastPos;
        private float _phase;
        private float _weight; // 0 = rest pose, 1 = full swing

        private void Awake()
        {
            _lastPos = transform.position;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            Vector3 delta = transform.position - _lastPos;
            delta.y = 0f;
            _lastPos = transform.position;

            float speed = delta.magnitude / dt;
            float targetWeight = speed > moveSpeedThreshold ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, targetWeight, blendSharpness * dt);

            if (_weight > 0.0001f)
                _phase += swingSpeed * dt;

            float swing = Mathf.Sin(_phase) * swingAmplitude * _weight;

            SetSwing(leftArm, swing);
            SetSwing(rightLeg, swing);
            SetSwing(rightArm, -swing);
            SetSwing(leftLeg, -swing);

            // Face the direction of travel while actually moving; never forced while still.
            if (bodyVisual != null && speed > moveSpeedThreshold && delta.sqrMagnitude > 1e-8f)
            {
                Quaternion desired = Quaternion.LookRotation(delta.normalized, Vector3.up);
                bodyVisual.rotation = Quaternion.RotateTowards(
                    bodyVisual.rotation, desired, bodyTurnSpeed * dt);
            }
        }

        private static void SetSwing(Transform limb, float degrees)
        {
            if (limb != null)
                limb.localRotation = Quaternion.Euler(degrees, 0f, 0f);
        }

        private void OnValidate()
        {
            swingAmplitude = Mathf.Max(0f, swingAmplitude);
            swingSpeed = Mathf.Max(0f, swingSpeed);
            moveSpeedThreshold = Mathf.Max(0f, moveSpeedThreshold);
            blendSharpness = Mathf.Max(0.01f, blendSharpness);
            bodyTurnSpeed = Mathf.Max(0f, bodyTurnSpeed);
        }
    }
}
