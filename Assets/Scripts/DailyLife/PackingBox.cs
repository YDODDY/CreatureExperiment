using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A packing box from the <see cref="WorkSupply"/>. Carried / placed / thrown like any Interactable.
    /// While it rests in the world a held <see cref="WorkItem"/> goes in with E (aim + Interact,
    /// <see cref="IOptionalHeldItemReceiver"/>; Empty -> HasItem). Sealing and marking are done with the held
    /// tools' Left Click, on the box itself:
    /// - a <see cref="TapeRoll"/> strip drawn across the top seam seals it (<see cref="TrySealWithTape"/>) -
    ///   the strip the player drew is the tape you see, crooked or not;
    /// - a <see cref="FragileStickerItem"/> stuck anywhere on it marks it fragile (<see cref="ApplyFragileSticker"/>).
    /// A wrong sticker is the player's mistake, judged on submission, not prevented here.
    ///
    /// Seal zone (logical, box space): a band <see cref="sealBandHalfWidth"/> either side of a centre line
    /// of the top (or bottom) face. The visible seam runs along local X; a line along local Z counts too, so a
    /// box turned on the table is not a trap. A strip on that face seals the box when at least
    /// <see cref="sealRequiredFraction"/> of the face width of it lies inside the band. Only a box with an item
    /// in it can be sealed; extra tape on a sealed box is just more tape.
    ///
    /// For any other held item it is not a receiver, so Swap / Place work as usual. The contained item is
    /// kept (inactive, parented here), never destroyed, so the submission can judge it.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class PackingBox : MonoBehaviour, IOptionalHeldItemReceiver
    {
        [Header("Legacy preset visuals (kept hidden - the real strip / sticker is the visual now)")]
        [SerializeField] private GameObject tapeVisual;
        [SerializeField] private GameObject stickerVisual;

        [Header("Seal zone")]
        [Tooltip("Half width (m) of the band along the top seam a strip has to run in.")]
        [SerializeField] private float sealBandHalfWidth = 0.1f;
        [Tooltip("Share of the face width the strip has to cover inside the band.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float sealRequiredFraction = 0.5f;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private WorkItem containedItem;
        [SerializeField] private bool isSealed;
        [SerializeField] private bool hasFragileSticker;
        [SerializeField] private bool isSubmitted;

        private Interactable _interactable;

        public WorkItem ContainedItem => containedItem;
        public bool HasItem => containedItem != null;
        public bool IsSealed => isSealed;
        public bool HasFragileSticker => hasFragileSticker;
        public bool IsSubmitted => isSubmitted;
        public Interactable Interactable => _interactable != null ? _interactable : (_interactable = GetComponent<Interactable>());

        private void Awake()
        {
            _interactable = GetComponent<Interactable>();
            RefreshVisuals();
        }

        // --- IOptionalHeldItemReceiver (work items only)
        public float MaxReach => 0f; // normal pickup reach

        public bool AppliesTo(Interactable item) => item != null && item.GetComponent<WorkItem>() != null;

        public bool CanReceive(Interactable item)
        {
            if (item == null || isSubmitted || Interactable.IsHeld)
                return false;
            var work = item.GetComponent<WorkItem>();
            return work != null && !HasItem && !work.IsResolved;
        }

        public string GetRejectPrompt(Interactable item)
        {
            if (item != null && item.GetComponent<WorkItem>() != null && HasItem)
                return "이미 물품이 들어 있습니다";
            return null;
        }

        public void Receive(Interactable item)
        {
            var work = item != null ? item.GetComponent<WorkItem>() : null;
            if (work == null)
                return;
            containedItem = work;
            work.transform.SetParent(transform, worldPositionStays: false);
            work.transform.localPosition = Vector3.zero;
            work.gameObject.SetActive(false);
            RefreshVisuals();
        }

        // --- IFocusTarget (shown while a work item is aimed at this box)
        public string FocusName => "물품 넣기";
        public Transform FocusTransform => transform;

        public void SetFocused(bool focused)
        {
            Interactable.SetFocused(focused);
        }

        // --- tools (Left Click)

        /// <summary>A tape strip was finished on this box. Seals it if the strip covers the top seam well enough. True if this sealed it.</summary>
        public bool TrySealWithTape(Vector3 worldStart, Vector3 worldEnd, Vector3 worldNormal)
        {
            if (isSealed || isSubmitted || !HasItem)
                return false;
            if (Mathf.Abs(Vector3.Dot(worldNormal.normalized, transform.up)) < 0.8f)
                return false; // top / bottom face only

            Vector3 scale = transform.lossyScale;
            Vector3 a = Vector3.Scale(transform.InverseTransformPoint(worldStart), scale);
            Vector3 b = Vector3.Scale(transform.InverseTransformPoint(worldEnd), scale);
            float halfX = 0.5f * Mathf.Abs(scale.x);
            float halfZ = 0.5f * Mathf.Abs(scale.z);

            // Seam along local X (the visible one), or along local Z.
            float alongX = ClippedLength(a.x, a.z, b.x, b.z, halfX, sealBandHalfWidth);
            float alongZ = ClippedLength(a.z, a.x, b.z, b.x, halfZ, sealBandHalfWidth);
            bool sealsX = alongX >= sealRequiredFraction * 2f * halfX;
            bool sealsZ = alongZ >= sealRequiredFraction * 2f * halfZ;
            if (!sealsX && !sealsZ)
                return false;

            isSealed = true;
            RefreshVisuals();
            return true;
        }

        /// <summary>A fragile sticker was stuck on this box.</summary>
        public void ApplyFragileSticker()
        {
            if (isSubmitted)
                return;
            hasFragileSticker = true;
            RefreshVisuals();
        }

        /// <summary>Called by the Completed receiver when this box is handed in.</summary>
        public void MarkSubmitted()
        {
            isSubmitted = true;
        }

        private void RefreshVisuals()
        {
            if (tapeVisual != null) tapeVisual.SetActive(false);
            if (stickerVisual != null) stickerVisual.SetActive(false);

            string label;
            if (!HasItem) label = "빈 포장상자";
            else if (!isSealed) label = "포장상자 (테이프 전)";
            else label = hasFragileSticker ? "포장 완료 (취급주의)" : "포장 완료";
            Interactable.SetDisplayName(label);
        }

        // Length of segment (u0,v0)-(u1,v1) inside the rectangle |u| <= halfU, |v| <= halfV (Liang-Barsky).
        private static float ClippedLength(float u0, float v0, float u1, float v1, float halfU, float halfV)
        {
            float du = u1 - u0, dv = v1 - v0;
            float t0 = 0f, t1 = 1f;
            if (!Clip(-du, u0 + halfU, ref t0, ref t1)) return 0f;
            if (!Clip(du, halfU - u0, ref t0, ref t1)) return 0f;
            if (!Clip(-dv, v0 + halfV, ref t0, ref t1)) return 0f;
            if (!Clip(dv, halfV - v0, ref t0, ref t1)) return 0f;
            return (t1 - t0) * Mathf.Sqrt(du * du + dv * dv);
        }

        private static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Approximately(p, 0f))
                return q >= 0f;
            float r = q / p;
            if (p < 0f)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }
    }
}
