using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The shared "contents are out" visuals for anything with liquid in it - a drink (<see cref="DrinkContainer"/>), a cup
    /// noodle (<see cref="SpillableContents"/>): a short splash (an <see cref="EggSplat"/> burst, no stain) and a flat puddle in
    /// the contents' colour lying on a fixed surface (a <see cref="TemporaryMess"/> template - gone the next day), riding that
    /// surface via <see cref="SurfaceMount"/>. Visual only: no colliders, no fluid.
    /// </summary>
    public static class LiquidSpill
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>One splash burst at <paramref name="point"/>, facing <paramref name="normal"/>.</summary>
        public static void Splash(EggSplat template, Vector3 point, Vector3 normal, string objectName = "DrinkSplash")
        {
            if (template == null)
                return;
            EggSplat fx = Object.Instantiate(template, point + normal * 0.02f, Quaternion.FromToRotation(Vector3.up, normal));
            fx.name = objectName;
            fx.gameObject.SetActive(true);
            fx.Play(leaveStain: false);
        }

        /// <summary>A puddle of <paramref name="color"/> (diameter within <paramref name="size"/>) on <paramref name="surface"/> at <paramref name="point"/>.</summary>
        public static void Puddle(GameObject template, Color color, Vector2 size, Vector3 point, Vector3 normal, Collider surface,
            float surfaceOffset = 0.004f, string objectName = "DrinkSpill")
        {
            if (template == null)
                return;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);
            GameObject spill = Object.Instantiate(template, point + normal * surfaceOffset, rot);
            spill.name = objectName;
            float d = Random.Range(size.x, size.y);
            Vector3 s = template.transform.localScale;
            spill.transform.localScale = new Vector3(d, s.y, d * Random.Range(0.75f, 1f));
            var block = new MaterialPropertyBlock();
            foreach (var r in spill.GetComponentsInChildren<Renderer>(true))
            {
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                r.SetPropertyBlock(block);
            }
            spill.SetActive(true);
            SurfaceMount.Attach(spill.transform, surface);
        }

        /// <summary>A puddle on the first fixed surface right under <paramref name="origin"/> (a leak where it stands). False if none.</summary>
        public static bool PuddleBelow(Transform origin, GameObject template, Color color, Vector2 size, string objectName = "DrinkSpill")
        {
            foreach (var hit in Physics.RaycastAll(origin.position + Vector3.up * 0.05f, Vector3.down, 1.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody != null || hit.collider.transform.IsChildOf(origin))
                    continue; // itself, the knife, other items
                Puddle(template, color, size, hit.point, hit.normal, hit.collider, objectName: objectName);
                return true;
            }
            return false;
        }
    }
}
