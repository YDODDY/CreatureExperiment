using System.Collections.Generic;
using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Keeps track of the marks the player leaves on surfaces during a day - "취급주의" stickers
    /// (<see cref="Place"/>, called by <see cref="FragileStickerItem"/>) and tape strips (<see cref="Track"/>,
    /// called by <see cref="TapeRoll"/>) - and removes them all on a new day (<see cref="WorkShiftController"/>
    /// calls <see cref="Clear"/>). A mark rides the surface it was put on (<see cref="SurfaceMount.ForMark"/> -
    /// a door leaf's hinge, a packing box); marks on static geometry with no mount sit under this object.
    /// Visual only - no collider, no Rigidbody.
    /// </summary>
    public class RuntimeStickerRoot : MonoBehaviour
    {
        [SerializeField] private Material stickerMaterial;
        [Tooltip("Edge length of a placed sticker, in metres.")]
        [SerializeField] private float size = 0.18f;
        [Tooltip("Gap from the surface, to avoid z-fighting.")]
        [SerializeField] private float surfaceOffset = 0.004f;

        private readonly List<GameObject> _marks = new List<GameObject>();

        /// <summary>Stick one sticker where <paramref name="hit"/> landed, facing out along its normal, riding that surface.</summary>
        public GameObject Place(RaycastHit hit)
        {
            var sticker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sticker.name = "FragileSticker_Placed";
            DestroyImmediate(sticker.GetComponent<Collider>());

            Vector3 normal = hit.normal;
            Vector3 up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            sticker.transform.SetPositionAndRotation(hit.point + normal * surfaceOffset, Quaternion.LookRotation(normal, up));
            sticker.transform.localScale = new Vector3(size, size, 0.002f);
            Transform mount = SurfaceMount.ForMark(hit.collider);
            sticker.transform.SetParent(mount != null ? mount : transform, worldPositionStays: true);

            var r = sticker.GetComponent<Renderer>();
            if (r != null)
            {
                if (stickerMaterial != null)
                    r.sharedMaterial = stickerMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            Track(sticker);
            return sticker;
        }

        /// <summary>Register a mark made elsewhere (a tape strip) so a new day removes it too.</summary>
        public void Track(GameObject mark)
        {
            if (mark != null)
                _marks.Add(mark);
        }

        /// <summary>Remove every mark (new day).</summary>
        public void Clear()
        {
            foreach (GameObject mark in _marks)
                if (mark != null)
                    Destroy(mark);
            _marks.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }
    }
}
