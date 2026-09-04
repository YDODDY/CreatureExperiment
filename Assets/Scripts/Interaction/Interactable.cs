using UnityEngine;

namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// Marker for objects that can be picked up and carried (by the player, or by a creature) and
    /// placed or thrown. It only carries the data the interaction system needs right now, plus a
    /// single-slot <see cref="Holder"/> so two carriers cannot hold the same object at once.
    /// Per-object "use" behaviour is intentionally left out until it is needed.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class Interactable : MonoBehaviour
    {
        [Header("Display")]
        [Tooltip("Name shown to the player when this object is focused. Falls back to the GameObject name.")]
        [SerializeField] private string displayName;

        [Header("Carry pose")]
        [Tooltip("Local position offset from the hold anchor while carried.")]
        [SerializeField] private Vector3 holdPositionOffset = Vector3.zero;
        [Tooltip("Local euler rotation offset from the hold anchor while carried.")]
        [SerializeField] private Vector3 holdRotationOffset = Vector3.zero;

        [Header("Focus feedback")]
        [Tooltip("Renderer of the outline child. Enabled only while this object is the player's focus.")]
        [SerializeField] private Renderer outlineRenderer;

        private Rigidbody _body;

        /// <summary>Rigidbody on this object (cached).</summary>
        public Rigidbody Body => _body != null ? _body : (_body = GetComponent<Rigidbody>());

        public string DisplayName => string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;

        public Vector3 HoldPositionOffset => holdPositionOffset;
        public Quaternion HoldRotationOffset => Quaternion.Euler(holdRotationOffset);

        /// <summary>
        /// Whoever is currently carrying this object (a <c>PlayerInteractor</c> or a creature's pickup
        /// component), or null while it sits free in the world. Just enough state for one holder at a
        /// time - not an ownership system. Set only through <see cref="TryGrab"/> / <see cref="Release"/>.
        /// </summary>
        public Object Holder { get; private set; }

        /// <summary>True while someone holds this object. A plain pickup must ignore objects for which this is true.</summary>
        public bool IsHeld => Holder != null;

        /// <summary>
        /// Claim this object for <paramref name="holder"/>. Returns false (and changes nothing) if a
        /// different holder already has it - this is what stops the player and the creature grabbing
        /// the same object, and what a future Take / Snatch action would deliberately bypass.
        /// Re-claiming with the same holder is a no-op that returns true.
        /// </summary>
        public bool TryGrab(Object holder)
        {
            if (holder == null || (Holder != null && Holder != holder))
                return false;
            Holder = holder;
            return true;
        }

        /// <summary>Drop the claim, but only if <paramref name="holder"/> is the one that holds it right now.</summary>
        public void Release(Object holder)
        {
            if (Holder == holder)
                Holder = null;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            if (outlineRenderer != null)
                outlineRenderer.enabled = false;
        }

        /// <summary>Turn the outline on or off. Called by <c>PlayerInteractor</c> as focus changes.</summary>
        public void SetFocused(bool focused)
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = focused;
        }
    }
}
