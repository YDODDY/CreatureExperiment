using UnityEngine;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Wall collision (0.1): the smallest possible "the creature does not walk through walls" pass.
    ///
    /// Every creature mover - Retreat / Approach / Inspect in <see cref="CreatureMovement"/>, the
    /// dev-test <see cref="CreatureJump"/> / <see cref="CreatureDash"/>, and the pattern-driven
    /// <see cref="CreatureProbe"/> / <see cref="CreatureThrowProbe"/> - ultimately just writes the
    /// creature root's <c>transform.position</c> directly (CreatureMovement owns X/Z, CreatureJump
    /// owns Y). None of them consult the physics scene. This component adds one corrective step
    /// AFTER all of them have run for the frame: it looks for the creature's own
    /// <see cref="CapsuleCollider"/> overlapping anything on <see cref="wallMask"/> and pushes the
    /// root straight back out along the shallowest separating axis
    /// (<see cref="Physics.ComputePenetration"/>). Because the movers only ever advance a small
    /// delta per frame, what is left after the push-out is the slide-along-the-wall component, so
    /// the creature stops dead at a wall instead of passing through it.
    ///
    /// Deliberately NOT here, by request: no NavMesh, no pathfinding, no "walk around the wall to
    /// reach a target behind it", no long-term "this creature is allowed to phase through that"
    /// decision layer. It also never moves, retargets or disables a mover - they keep running
    /// exactly as before; this only ever un-does penetration after the fact, so Retreat / Approach /
    /// Inspect / Jump / Dash / Probe are all still intact, just physically bounded.
    ///
    /// Y is left untouched (the vertical component of every push-out is zeroed) so it never fights
    /// CreatureJump for the root's Y - a vertical wall produces a purely horizontal push-out anyway;
    /// zeroing Y just means a floor / ceiling that happens to be in <see cref="wallMask"/> is
    /// ignored rather than launching or burying the creature.
    /// </summary>
    [RequireComponent(typeof(CapsuleCollider))]
    [DefaultExecutionOrder(200)] // run after CreatureMovement / CreatureJump / the probes have moved the root this frame
    public class CreatureWallCollision : MonoBehaviour
    {
        [Header("What counts as a wall")]
        [Tooltip("Layers the creature may not pass through this pass. Default is Everything; horizontal " +
                 "surfaces (floor/ceiling) are ignored automatically. Once some props are meant to be " +
                 "phase-through, put the real walls on their own layer and select only that layer here.")]
        [SerializeField] private LayerMask wallMask = ~0;

        [Header("Solver")]
        [Tooltip("Push-out passes per frame. 2-4 is plenty for a room of flat walls; more only helps in a tight corner.")]
        [SerializeField] private int maxIterations = 4;
        [Tooltip("Extra gap left between the creature and the wall after push-out, in metres. Stops immediate re-penetration jitter.")]
        [SerializeField] private float skinWidth = 0.015f;
        [Tooltip("Ignore any push-out whose flat (XZ) length is below this - it is effectively a floor / ceiling hit. Metres.")]
        [SerializeField] private float minHorizontalPush = 0.0005f;

        private CapsuleCollider _capsule;
        private readonly Collider[] _overlaps = new Collider[8];

        private void Awake()
        {
            _capsule = GetComponent<CapsuleCollider>();
        }

        private void LateUpdate()
        {
            if (_capsule == null || !_capsule.enabled)
                return;

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                if (!ResolveOnce())
                    return;
            }
        }

        // One overlap query + push-out. Returns true if it moved the creature (so another pass is worthwhile).
        private bool ResolveOnce()
        {
            GetWorldCapsule(out Vector3 p0, out Vector3 p1, out float radius);

            int count = Physics.OverlapCapsuleNonAlloc(
                p0, p1, radius, _overlaps, wallMask, QueryTriggerInteraction.Ignore);

            bool moved = false;

            for (int i = 0; i < count; i++)
            {
                Collider other = _overlaps[i];
                if (other == null || other == _capsule)
                    continue;
                // Never depenetrate from our own body or an object parented under us (e.g. a carried pickup).
                if (other.transform == transform || other.transform.IsChildOf(transform))
                    continue;

                if (!Physics.ComputePenetration(
                        _capsule, transform.position, transform.rotation,
                        other, other.transform.position, other.transform.rotation,
                        out Vector3 dir, out float distance))
                    continue;

                dir.y = 0f;
                float flat = dir.magnitude;
                if (flat < minHorizontalPush)
                    continue; // essentially a floor / ceiling hit - leave Y to CreatureJump

                dir /= flat;
                transform.position += dir * (distance + skinWidth);
                moved = true;
            }

            return moved;
        }

        // World-space capsule segment endpoints and radius, honouring the collider's centre, height,
        // direction and the transform's lossy scale (uniform in this prototype; handled generally anyway).
        private void GetWorldCapsule(out Vector3 p0, out Vector3 p1, out float radius)
        {
            Vector3 scale = transform.lossyScale;
            float absX = Mathf.Abs(scale.x), absY = Mathf.Abs(scale.y), absZ = Mathf.Abs(scale.z);

            Vector3 axis;
            float radialScale, heightScale;
            switch (_capsule.direction)
            {
                case 0: axis = transform.right;   radialScale = Mathf.Max(absY, absZ); heightScale = absX; break;
                case 2: axis = transform.forward; radialScale = Mathf.Max(absX, absY); heightScale = absZ; break;
                default: axis = transform.up;     radialScale = Mathf.Max(absX, absZ); heightScale = absY; break;
            }

            radius = _capsule.radius * radialScale;
            float half = Mathf.Max(0f, _capsule.height * 0.5f * heightScale - radius);

            Vector3 centre = transform.TransformPoint(_capsule.center);
            p0 = centre + axis * half;
            p1 = centre - axis * half;
        }

        private void OnValidate()
        {
            maxIterations = Mathf.Max(1, maxIterations);
            skinWidth = Mathf.Max(0f, skinWidth);
            minHorizontalPush = Mathf.Max(0f, minHorizontalPush);
        }
    }
}
