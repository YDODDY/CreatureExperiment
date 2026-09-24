using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Parent of the "취급주의" stickers the player sticks on walls / floors / ceilings during a day.
    /// <see cref="StickerSurface"/> asks it to <see cref="Place"/> one; <see cref="WorkShiftController"/>
    /// clears them all on a new day. Visual only - no collider, no gameplay meaning.
    /// </summary>
    public class RuntimeStickerRoot : MonoBehaviour
    {
        [SerializeField] private Material stickerMaterial;
        [Tooltip("Edge length of a placed sticker, in metres.")]
        [SerializeField] private float size = 0.18f;
        [Tooltip("Gap from the surface, to avoid z-fighting.")]
        [SerializeField] private float surfaceOffset = 0.004f;

        /// <summary>Stick one sticker at <paramref name="point"/>, facing out along <paramref name="normal"/>.</summary>
        public void Place(Vector3 point, Vector3 normal)
        {
            var sticker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sticker.name = "FragileSticker_Placed";
            Destroy(sticker.GetComponent<Collider>());

            Vector3 up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            sticker.transform.SetPositionAndRotation(point + normal * surfaceOffset, Quaternion.LookRotation(normal, up));
            sticker.transform.localScale = new Vector3(size, size, 0.002f);
            sticker.transform.SetParent(transform, worldPositionStays: true);

            var r = sticker.GetComponent<Renderer>();
            if (r != null && stickerMaterial != null)
                r.sharedMaterial = stickerMaterial;
        }

        /// <summary>Remove every placed sticker (new day).</summary>
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }
    }
}
