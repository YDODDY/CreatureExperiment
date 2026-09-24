using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Where an impact result (a stuck knife, an egg stain) should ride so it moves with the surface it hit:
    /// the hit collider's own transform, or - when that one is stretched (a non-uniformly scaled cube leaf or
    /// wall) - the nearest uniformly scaled ancestor: the hinge a SwingDoor rotates, the room a wall stands in.
    /// Parenting there keeps the child undistorted while it follows every move / turn of the surface.
    /// </summary>
    internal static class SurfaceMount
    {
        public static Transform For(Collider surface)
        {
            for (Transform t = surface != null ? surface.transform : null; t != null; t = t.parent)
                if (IsUniform(t.lossyScale))
                    return t;
            return null;
        }

        /// <summary>Parent <paramref name="obj"/> to the mount for <paramref name="surface"/>, keeping its world pose.</summary>
        public static void Attach(Transform obj, Collider surface) => obj.SetParent(For(surface), worldPositionStays: true);

        private static bool IsUniform(Vector3 s)
        {
            float x = Mathf.Abs(s.x), y = Mathf.Abs(s.y), z = Mathf.Abs(s.z);
            float max = Mathf.Max(x, Mathf.Max(y, z));
            float min = Mathf.Min(x, Mathf.Min(y, z));
            return min > 0f && max - min <= max * 0.001f;
        }
    }
}
