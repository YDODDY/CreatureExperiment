using UnityEngine;
using UnityEngine.Rendering;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A reusable roll of packing tape (never runs out). Carried like any Interactable (E / F / Right Click).
    /// Left Click while held draws a strip (<see cref="IHeldPrimaryAction"/>, press / hold / release):
    /// - press: the camera centre ray must hit a solid surface within <see cref="reach"/> - that point and
    ///   normal start the strip and a preview appears;
    /// - hold: the ray is followed; while it stays on the same surface (same collider, or same Rigidbody)
    ///   facing the same way, the end follows it, projected onto the start plane and clamped to
    ///   <see cref="maxLength"/>. Off that surface the end simply stays at its last good point - one strip is
    ///   one flat run on one surface, no wrapping round corners;
    /// - release: at least <see cref="minLength"/> long it becomes a TapeStrip, otherwise it is dropped.
    /// The strip rides the surface it is on (<see cref="SurfaceMount.ForMark"/> - a door leaf's hinge, a
    /// packing box) and is handed to <see cref="RuntimeStickerRoot"/> so a new day clears it. On a
    /// <see cref="PackingBox"/> the finished strip is offered to <see cref="PackingBox.TrySealWithTape"/>.
    ///
    /// A strip is a visual trace only: no collider, no Rigidbody, no joint - it holds nothing together and
    /// never stops a door. The roll stays in the hand.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class TapeRoll : MonoBehaviour, IHeldPrimaryAction
    {
        [Header("References")]
        [Tooltip("Tracks finished strips so a new day removes them.")]
        [SerializeField] private RuntimeStickerRoot markRoot;
        [SerializeField] private Material tapeMaterial;

        [Header("Drawing")]
        [SerializeField] private float reach = 1.5f;
        [SerializeField] private float minLength = 0.06f;
        [SerializeField] private float maxLength = 1.0f;
        [Tooltip("How closely the aimed surface must face the start normal to count as the same flat surface.")]
        [SerializeField] private float sameSurfaceNormalDot = 0.9f;
        [SerializeField] private string playerTag = "Player";

        [Header("Strip")]
        [SerializeField] private float width = 0.05f;
        [SerializeField] private float thickness = 0.001f;
        [Tooltip("Gap from the surface, against z-fighting.")]
        [SerializeField] private float surfaceOffset = 0.002f;

        private bool _drawing;
        private Collider _surface;
        private Transform _mount;       // what the strip rides; null = world
        private Vector3 _startLocal;    // in mount space
        private Vector3 _normalLocal;
        private Vector3 _endLocal;
        private GameObject _preview;

        public bool PrimaryActive => _drawing;

        private Vector3 StartWorld => _mount != null ? _mount.TransformPoint(_startLocal) : _startLocal;
        private Vector3 EndWorld => _mount != null ? _mount.TransformPoint(_endLocal) : _endLocal;
        private Vector3 NormalWorld => (_mount != null ? _mount.TransformDirection(_normalLocal) : _normalLocal).normalized;

        private void OnDisable() => PrimaryCancel();

        public bool PrimaryPress(Ray aim)
        {
            PrimaryCancel();
            if (!Physics.Raycast(aim, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore))
                return false;
            if (!string.IsNullOrEmpty(playerTag) && hit.collider.CompareTag(playerTag))
                return false;

            _surface = hit.collider;
            _mount = SurfaceMount.ForMark(_surface);
            if (_mount == null && markRoot != null)
                _mount = markRoot.transform;
            _startLocal = ToMountPoint(hit.point);
            _endLocal = _startLocal;
            _normalLocal = ToMountDir(hit.normal);

            _preview = CreateStrip();
            _drawing = true;
            RefreshStrip();
            return true;
        }

        public void PrimaryHold(Ray aim)
        {
            if (!_drawing)
                return;
            if (_surface == null || !_surface.enabled || !_surface.gameObject.activeInHierarchy || _preview == null)
            {
                PrimaryCancel();
                return;
            }

            if (Physics.Raycast(aim, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore) && IsSameSurface(hit))
            {
                Vector3 start = StartWorld;
                Vector3 n = NormalWorld;
                Vector3 p = hit.point - n * Vector3.Dot(hit.point - start, n); // onto the start plane
                Vector3 d = p - start;
                if (d.magnitude > maxLength)
                    p = start + d.normalized * maxLength;
                _endLocal = ToMountPoint(p);
            }
            // else: keep the last good end point
            RefreshStrip();
        }

        public void PrimaryRelease(Ray aim)
        {
            if (!_drawing)
                return;

            Vector3 start = StartWorld, end = EndWorld, normal = NormalWorld;
            if (_preview == null || (end - start).magnitude < minLength)
            {
                PrimaryCancel();
                return;
            }

            GameObject strip = _preview;
            strip.name = "TapeStrip";
            if (markRoot != null)
                markRoot.Track(strip);

            var box = _surface != null ? _surface.GetComponentInParent<PackingBox>() : null;
            _preview = null;
            _drawing = false;
            _surface = null;
            _mount = null;

            if (box != null)
                box.TrySealWithTape(start, end, normal);
        }

        public void PrimaryCancel()
        {
            if (_preview != null)
                Destroy(_preview);
            _preview = null;
            _drawing = false;
            _surface = null;
            _mount = null;
        }

        private bool IsSameSurface(RaycastHit hit)
        {
            bool same = hit.collider == _surface
                || (_surface.attachedRigidbody != null && hit.collider.attachedRigidbody == _surface.attachedRigidbody);
            return same && Vector3.Dot(hit.normal, NormalWorld) >= sameSurfaceNormalDot;
        }

        private GameObject CreateStrip()
        {
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "TapeStrip_Preview";
            DestroyImmediate(strip.GetComponent<Collider>());
            var r = strip.GetComponent<Renderer>();
            if (r != null)
            {
                if (tapeMaterial != null)
                    r.sharedMaterial = tapeMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (_mount != null)
                strip.transform.SetParent(_mount, worldPositionStays: false);
            return strip;
        }

        // Lay the strip flat on the surface from start to end: local z along the run, local y along the normal.
        private void RefreshStrip()
        {
            if (_preview == null)
                return;

            Vector3 start = StartWorld, end = EndWorld, n = NormalWorld;
            Vector3 run = end - start;
            float length = run.magnitude;
            Vector3 dir = length > 1e-4f ? run / length : Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;

            _preview.transform.SetPositionAndRotation(
                (start + end) * 0.5f + n * (surfaceOffset + thickness * 0.5f),
                Quaternion.LookRotation(dir, n));

            Vector3 world = new Vector3(width, thickness, Mathf.Max(length, 0.002f));
            float s = _preview.transform.parent != null ? _preview.transform.parent.lossyScale.x : 1f;
            _preview.transform.localScale = s > 1e-6f ? world / s : world;
        }

        private Vector3 ToMountPoint(Vector3 world) => _mount != null ? _mount.InverseTransformPoint(world) : world;
        private Vector3 ToMountDir(Vector3 world) => _mount != null ? _mount.InverseTransformDirection(world) : world;
    }
}
