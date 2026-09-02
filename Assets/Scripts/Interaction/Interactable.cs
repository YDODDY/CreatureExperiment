using UnityEngine;

namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// Marker for objects the player can pick up, carry, place and throw.
    /// It only carries the data the interaction system needs right now.
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
