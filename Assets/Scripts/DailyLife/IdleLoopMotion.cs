using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A fake idle animation for a primitive NPC: rotates one pivot (an arm at the shoulder) in a small endless loop
    /// around its base pose - the bartender's hand circling a rag over a glass, a counter clerk's arms swaying. Optionally
    /// spins a held prop slowly (the glass being polished). Purely visual; no colliders move with it that matter.
    /// </summary>
    public class IdleLoopMotion : MonoBehaviour
    {
        [Tooltip("The rotated transform (an arm pivot). Empty = this transform.")]
        [SerializeField] private Transform pivot;
        [Tooltip("Local euler angles of the resting pose.")]
        [SerializeField] private Vector3 baseEuler;
        [Tooltip("Degrees of the loop: x follows a sine, z a cosine (a circle), y half speed.")]
        [SerializeField] private Vector3 amplitude = new Vector3(8f, 0f, 10f);
        [SerializeField] private float cyclesPerSecond = 1f;
        [Tooltip("Phase offset in cycles (so two NPCs / two arms are not in step).")]
        [SerializeField] private float phase;
        [Tooltip("Optional prop spun around its own up axis (deg/s).")]
        [SerializeField] private Transform spinProp;
        [SerializeField] private float spinSpeed = 60f;

        private Transform Pivot => pivot != null ? pivot : transform;

        private void Update()
        {
            float t = (Time.time * cyclesPerSecond + phase) * Mathf.PI * 2f;
            Pivot.localRotation = Quaternion.Euler(baseEuler + new Vector3(
                amplitude.x * Mathf.Sin(t), amplitude.y * Mathf.Sin(t * 0.5f), amplitude.z * Mathf.Cos(t)));
            if (spinProp != null)
                spinProp.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        }
    }
}
