using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// First-person "look at it and press Interact to use it" for non-pickup objects (door, lid, card
    /// terminal, bed). Owns what counts as a World Use target and its reach (<see cref="TryGetUsable"/>).
    /// When a <see cref="PlayerInteractor"/> is present it owns the Interact key and routes a press here
    /// (World Use first, held item or not), so this component does not read the key itself - one press
    /// can never both Use and Place. Without an interactor it reads Interact on its own as before.
    /// </summary>
    public class PlayerActivator : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("References")]
        [Tooltip("Ray origin - usually the Main Camera.")]
        [SerializeField] private Transform aimSource;
        [Tooltip("If set, it owns the Interact key and asks this component for World Use targets.")]
        [SerializeField] private PlayerInteractor interactor;

        [Header("Reach")]
        [SerializeField] private float useRange = 2.2f;
        [SerializeField] private LayerMask useMask = ~0;

        private InputAction _interact;

        /// <summary>Max distance at which an <see cref="IUsable"/> can be used.</summary>
        public float UseRange => useRange;

        private void Awake()
        {
            if (inputActions != null)
            {
                var map = inputActions.FindActionMap("Player", throwIfNotFound: false);
                _interact = map?.FindAction("Interact", throwIfNotFound: false);
            }
            if (aimSource == null && Camera.main != null)
                aimSource = Camera.main.transform;
            if (interactor == null)
                interactor = GetComponent<PlayerInteractor>();
        }

        private void OnEnable() => _interact?.Enable();
        private void OnDisable() => _interact?.Disable();

        /// <summary>The <see cref="IUsable"/> on the object <paramref name="hit"/> struck, if it is within reach.</summary>
        public bool TryGetUsable(RaycastHit hit, out IUsable usable)
        {
            usable = null;
            if (hit.collider == null || hit.distance > useRange)
                return false;
            if ((useMask.value & (1 << hit.collider.gameObject.layer)) == 0)
                return false;
            usable = hit.collider.GetComponentInParent<IUsable>();
            // A loose object attached below a usable (a knife stuck in a door) is itself the target, not the door.
            var item = hit.collider.GetComponentInParent<Interactable>();
            if (item != null && usable is Component owner && item.transform != owner.transform && item.transform.IsChildOf(owner.transform))
                usable = null;
            return usable as Object != null;
        }

        private void Update()
        {
            // The interactor routes Interact (including World Use) - never handle the same press twice.
            if (interactor != null)
                return;
            if (_interact == null || aimSource == null)
                return;
            if (!_interact.WasPressedThisFrame())
                return;

            var ray = new Ray(aimSource.position, aimSource.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, useRange, useMask, QueryTriggerInteraction.Ignore)
                && TryGetUsable(hit, out IUsable usable))
                usable.Use();
        }
    }
}
