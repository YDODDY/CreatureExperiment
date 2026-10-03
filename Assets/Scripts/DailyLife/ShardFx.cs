using UnityEngine;
using UnityEngine.Rendering;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The pieces of a <see cref="Breakable"/>: small primitive chips thrown out from the break point, falling until they
    /// reach the floor found under the break (one raycast at spawn), lying there, then shrinking away. Moved by hand -
    /// no colliders or Rigidbodies, so they never get in the way of aiming, placing or physics.
    /// </summary>
    public class ShardFx : MonoBehaviour
    {
        private const float Gravity = 9.81f;

        private Transform[] _bits;
        private Vector3[] _velocity;
        private Vector3[] _spin;
        private Vector3[] _scale;
        private bool[] _resting;
        private float _floorY;
        private float _life;
        private float _t;

        public static void Spawn(Transform source, Vector3 point, Material material, int count, float size, float lifetime)
        {
            if (count <= 0)
                return;
            var go = new GameObject("BreakShards");
            go.transform.position = point;
            var fx = go.AddComponent<ShardFx>();
            fx._life = Mathf.Max(0.5f, lifetime);
            fx._floorY = FindFloor(source, point);
            fx._bits = new Transform[count];
            fx._velocity = new Vector3[count];
            fx._spin = new Vector3[count];
            fx._scale = new Vector3[count];
            fx._resting = new bool[count];
            for (int i = 0; i < count; i++)
            {
                var bit = GameObject.CreatePrimitive(i % 3 == 0 ? PrimitiveType.Quad : PrimitiveType.Cube);
                bit.name = "Shard";
                Destroy(bit.GetComponent<Collider>());
                var r = bit.GetComponent<Renderer>();
                if (material != null)
                    r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                bit.transform.SetParent(go.transform, worldPositionStays: false);
                bit.transform.localPosition = UnityEngine.Random.insideUnitSphere * size;
                bit.transform.rotation = UnityEngine.Random.rotation;
                float s = size * UnityEngine.Random.Range(0.5f, 1.3f);
                fx._scale[i] = new Vector3(s, s * UnityEngine.Random.Range(0.2f, 0.45f), s * UnityEngine.Random.Range(0.6f, 1.2f));
                bit.transform.localScale = fx._scale[i];
                fx._bits[i] = bit.transform;
                Vector3 dir = UnityEngine.Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.8f + 0.2f;
                fx._velocity[i] = dir * UnityEngine.Random.Range(0.8f, 2.6f);
                fx._spin[i] = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(200f, 720f);
            }
        }

        // The fixed surface under the break (no Rigidbody, not the breaking item itself).
        private static float FindFloor(Transform source, Vector3 point)
        {
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * 0.05f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody != null || (source != null && hit.collider.transform.IsChildOf(source)))
                    continue;
                return hit.point.y;
            }
            return point.y - 4f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;
            float fade = _t > _life - 0.5f ? Mathf.Clamp01((_life - _t) / 0.5f) : 1f;
            for (int i = 0; i < _bits.Length; i++)
            {
                Transform b = _bits[i];
                if (!_resting[i])
                {
                    _velocity[i].y -= Gravity * dt;
                    Vector3 p = b.position + _velocity[i] * dt;
                    float rest = _floorY + _scale[i].y * 0.5f;
                    if (p.y <= rest)
                    {
                        p.y = rest;
                        _resting[i] = true;
                        b.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                    }
                    else
                        b.Rotate(_spin[i] * dt, Space.Self);
                    b.position = p;
                }
                b.localScale = _scale[i] * fade;
            }
            if (_t >= _life)
                Destroy(gameObject);
        }
    }
}
