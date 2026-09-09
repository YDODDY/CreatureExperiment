using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// First-person "look at it and press Interact to use it" for non-pickup objects (card terminal,
    /// bed). Reuses the shared <c>Interact</c> action. Defers whenever <see cref="PlayerInteractor"/>
    /// is holding an item - then Interact means "place" - and since usables have no <c>Interactable</c>
    /// the two never aim at the same object anyway.
    /// </summary>
    public class PlayerActivator : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("References")]
        [Tooltip("Ray origin - usually the Main Camera.")]
        [SerializeField] private Transform aimSource;
        [Tooltip("If set, the Interact key is ignored here while this component is holding an item.")]
        [SerializeField] private PlayerInteractor interactor;

        [Header("Reach")]
        [SerializeField] private float useRange = 2.2f;
        [SerializeField] private LayerMask useMask = ~0;

        private InputAction _interact;

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

        private void Update()
        {
            if (_interact == null || aimSource == null)
                return;
            if (!_interact.WasPressedThisFrame())
                return;
            if (interactor != null && interactor.IsHolding)
                return;

            var ray = new Ray(aimSource.position, aimSource.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, useRange, useMask, QueryTriggerInteraction.Ignore))
            {
                var usable = hit.collider.GetComponentInParent<IUsable>();
                usable?.Use();
            }
        }
    }
}
