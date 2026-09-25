using UnityEngine;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A corridor ceiling light that switches on when the Player walks near it. "Motion" means only the
    /// Player's own Move input (<see cref="PlayerMovement.HasMovementInput"/>) while the Player stands
    /// inside this light's detection range - mouse look, Interact, throws, doors, physics objects and
    /// anything else never refresh it. Each detection restarts a <see cref="motionHoldSeconds"/> timer;
    /// the light stays on until that long passes with no new detection, whether the Player stopped or
    /// walked out of range. Every SensorLight keeps its own range and timer, so overlapping ranges simply
    /// light several at once.
    ///
    /// Range = a horizontal radius around the fixture, limited to the floor directly under it (the
    /// Player's feet must be between <see cref="minDepthBelow"/> and <see cref="maxDepthBelow"/> below the
    /// fixture, so floors above / below don't trigger it), plus an optional line-of-sight check so the
    /// Player moving on the other side of a wall doesn't count. Starts off.
    /// </summary>
    public class SensorLight : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Light targetLight;
        [Tooltip("Fixture surface that shows the on / off material.")]
        [SerializeField] private Renderer fixtureSurface;
        [SerializeField] private Material onMaterial;
        [SerializeField] private Material offMaterial;

        [Header("Detection")]
        [Tooltip("Found automatically when left empty.")]
        [SerializeField] private PlayerMovement player;
        [Tooltip("Horizontal distance from the fixture (m).")]
        [SerializeField] private float detectionRadius = 5.5f;
        [Tooltip("The Player's feet must be at least this far below the fixture (m).")]
        [SerializeField] private float minDepthBelow = 0.5f;
        [Tooltip("...and at most this far below it (m) - roughly one storey.")]
        [SerializeField] private float maxDepthBelow = 3.4f;
        [Tooltip("Ignore the Player behind a wall (linecast from the fixture to the Player's chest).")]
        [SerializeField] private bool requireLineOfSight = true;
        [SerializeField] private LayerMask sightMask = ~0;
        [SerializeField] private float chestHeight = 1.0f;

        [Header("Timer")]
        [SerializeField] private float motionHoldSeconds = 5.0f;

        private bool _isOn;
        private float _lastMotionTime = float.NegativeInfinity;

        public bool IsOn => _isOn;
        public float LastMotionTime => _lastMotionTime;

        private void Awake()
        {
            if (player == null)
                player = FindFirstObjectByType<PlayerMovement>();
            SetOn(false);
        }

        private void Update()
        {
            if (player != null && player.HasMovementInput && IsInRange(player.transform))
                _lastMotionTime = Time.time;

            bool shouldBeOn = Time.time - _lastMotionTime < motionHoldSeconds;
            if (shouldBeOn != _isOn)
                SetOn(shouldBeOn);
        }

        private bool IsInRange(Transform target)
        {
            Vector3 feet = target.position;
            Vector3 origin = transform.position;

            float depth = origin.y - feet.y;
            if (depth < minDepthBelow || depth > maxDepthBelow)
                return false;

            float dx = feet.x - origin.x;
            float dz = feet.z - origin.z;
            if (dx * dx + dz * dz > detectionRadius * detectionRadius)
                return false;

            if (!requireLineOfSight)
                return true;

            Vector3 chest = feet + Vector3.up * chestHeight;
            // Start just under the fixture so the ceiling slab it is mounted on is never the first hit.
            if (!Physics.Linecast(origin + Vector3.down * 0.15f, chest, out RaycastHit hit, sightMask, QueryTriggerInteraction.Ignore))
                return true;
            return hit.transform == target || hit.transform.IsChildOf(target);
        }

        private void SetOn(bool on)
        {
            _isOn = on;
            if (targetLight != null)
                targetLight.enabled = on;
            if (fixtureSurface != null)
            {
                Material mat = on ? onMaterial : offMaterial;
                if (mat != null)
                    fixtureSurface.sharedMaterial = mat;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.8f);
            Vector3 top = transform.position + Vector3.down * minDepthBelow;
            Vector3 bottom = transform.position + Vector3.down * maxDepthBelow;
            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * detectionRadius;
                Vector3 p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * detectionRadius;
                Gizmos.DrawLine(top + p0, top + p1);
                Gizmos.DrawLine(bottom + p0, bottom + p1);
                if (i % 8 == 0)
                    Gizmos.DrawLine(top + p0, bottom + p0);
            }
        }
    }
}
