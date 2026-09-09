using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Player
{
    /// <summary>
    /// Minimal first-person interaction: look at an <see cref="Interactable"/> and press
    /// Interact (E) to pick it up, press Interact again to place it on the aimed ground
    /// spot within reach, or press Throw (F) to throw it along the camera forward.
    ///
    /// While nothing is held, the object under the centre ray and within
    /// <see cref="pickupRange"/> is the "focus": it gets a white outline and a name tag.
    /// The very same query decides what a pickup grabs, so the two never disagree.
    ///
    /// Throw is a purely physical action here. It carries no "attack" / "hostile" meaning;
    /// any interpretation of what a throw means belongs to a later, separate system.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("References")]
        [Tooltip("Transform the aim ray starts from. Usually the Main Camera.")]
        [SerializeField] private Transform aimSource;
        [Tooltip("Empty transform (child of the camera) the held object is parented to.")]
        [SerializeField] private Transform holdAnchor;
        [Tooltip("World-space name tag shown above the focused object.")]
        [SerializeField] private FocusLabel focusLabel;

        [Header("Pick up")]
        [Tooltip("Max distance to reach / focus an interactable when picking up.")]
        [SerializeField] private float pickupRange = 1.5f;
        [SerializeField] private LayerMask pickupMask = ~0;

        [Header("Place down")]
        [Tooltip("Ray length used when searching for a spot to place the held object.")]
        [SerializeField] private float placeRayRange = 6f;
        [Tooltip("Placement is only allowed within this distance from the player.")]
        [SerializeField] private float placeMaxDistance = 2.7f;
        [Tooltip("Placement is rejected on surfaces steeper than this (degrees from flat).")]
        [SerializeField] private float placeMaxSlope = 30f;
        [SerializeField] private LayerMask placeMask = ~0;

        [Header("Throw")]
        [Tooltip("Initial speed given to the object along the camera forward.")]
        [SerializeField] private float throwSpeed = 8f;
        [Tooltip("Small random spin added on throw. Set 0 for none.")]
        [SerializeField] private float throwSpin = 2f;

        private InputAction _interactAction;
        private InputAction _throwAction;

        private IFocusTarget _focus;
        private Interactable _held;
        private Collider[] _heldColliders;
        private float _heldPivotToBottom;

        // Per-frame placement solution while carrying.
        private bool _placeValid;
        private Vector3 _placePosition;
        private Quaternion _placeRotation;

        /// <summary>True while the player is carrying an item. A separate <c>PlayerActivator</c> reads this so the Interact key means "place" (here), not "use" (a card terminal / bed).</summary>
        public bool IsHolding => _held != null;

        private void Awake()
        {
            var playerMap = inputActions.FindActionMap("Player", throwIfNotFound: true);
            _interactAction = playerMap.FindAction("Interact", throwIfNotFound: true);
            _throwAction = playerMap.FindAction("Throw", throwIfNotFound: true);

            if (aimSource == null && Camera.main != null)
                aimSource = Camera.main.transform;
        }

        private void OnEnable()
        {
            _interactAction?.Enable();
            _throwAction?.Enable();
        }

        private void OnDisable()
        {
            _interactAction?.Disable();
            _throwAction?.Disable();
            SetFocus(null);
        }

        private void Update()
        {
            if (_held == null)
            {
                SetFocus(FindFocusInView());

                // Only pick up when the focus is an actual Interactable. A non-pickup focus target
                // (a FocusableProp on a card terminal / bed) gets the outline + name but its Interact
                // is handled by its own path (PlayerActivator -> IUsable).
                if (_focus is Interactable focusItem && _interactAction.WasPressedThisFrame())
                    Pickup(focusItem);
                return;
            }

            UpdatePlacementSolution();

            if (_throwAction.WasPressedThisFrame())
                Throw();
            else if (_interactAction.WasPressedThisFrame() && _placeValid)
                Place();
        }

        /// <summary>
        /// The single source of truth for "what is the player pointing at, in reach". Feeds the focus
        /// feedback and (when it is an <see cref="Interactable"/>) the pickup. Now also matches a
        /// non-pickup <see cref="IFocusTarget"/> (a card terminal / bed via FocusableProp).
        /// </summary>
        private IFocusTarget FindFocusInView()
        {
            var ray = new Ray(aimSource.position, aimSource.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, pickupRange, pickupMask, QueryTriggerInteraction.Ignore))
            {
                var target = hit.collider.GetComponentInParent<IFocusTarget>();
                // An object the creature is already holding is not a focus / pickup candidate.
                if (target is Interactable it && it.IsHeld)
                    return null;
                return target;
            }
            return null;
        }

        private void SetFocus(IFocusTarget next)
        {
            if (ReferenceEquals(next, _focus))
                return;

            // `as Object` so the null test is Unity-lifetime-aware (an IFocusTarget is always a
            // MonoBehaviour); never call SetFocused on a destroyed object.
            if (_focus as Object != null)
                _focus.SetFocused(false);

            _focus = next;

            if (_focus as Object != null)
            {
                _focus.SetFocused(true);
                if (focusLabel != null) focusLabel.Show(_focus);
            }
            else if (focusLabel != null)
            {
                focusLabel.Hide();
            }
        }

        private void Pickup(Interactable interactable)
        {
            // Claim it. Fails if the creature already holds it - a plain pickup never takes.
            if (!interactable.TryGrab(this))
                return;

            // A mid-flight catch ends the flight immediately - it is held now, not flying.
            interactable.SetInFlight(false);

            SetFocus(null);

            _held = interactable;
            _heldColliders = _held.GetComponentsInChildren<Collider>();

            // Cache how far the pivot sits above the object's lowest point, so we can
            // rest it flush on the ground later. Colliders are still enabled here.
            _heldPivotToBottom = ComputePivotToBottom(_held.transform, _heldColliders);

            foreach (var col in _heldColliders)
                if (col != null) col.enabled = false;

            var body = _held.Body;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;

            _held.transform.SetParent(holdAnchor, worldPositionStays: false);
            _held.transform.localPosition = _held.HoldPositionOffset;
            _held.transform.localRotation = _held.HoldRotationOffset;
        }

        private void UpdatePlacementSolution()
        {
            _placeValid = false;

            var ray = new Ray(aimSource.position, aimSource.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, placeRayRange, placeMask, QueryTriggerInteraction.Ignore))
                return;

            if (Vector3.Distance(transform.position, hit.point) > placeMaxDistance)
                return;

            if (Vector3.Angle(hit.normal, Vector3.up) > placeMaxSlope)
                return;

            _placePosition = hit.point + Vector3.up * _heldPivotToBottom;
            _placeRotation = Quaternion.Euler(0f, aimSource.eulerAngles.y, 0f);
            _placeValid = true;
        }

        private void Place()
        {
            Interactable obj = _held;
            _held = null;
            obj.Release(this);

            obj.transform.SetParent(null, worldPositionStays: true);
            obj.transform.SetPositionAndRotation(_placePosition, _placeRotation);
            EnableHeldColliders();

            var body = obj.Body;
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.Sleep();

            PhysicalEvents.Raise(PhysicalEventKind.Place, obj, this);
        }

        private void Throw()
        {
            Interactable obj = _held;
            _held = null;
            obj.Release(this);
            obj.SetInFlight(true);

            obj.transform.SetParent(null, worldPositionStays: true);
            EnableHeldColliders();

            var body = obj.Body;
            body.isKinematic = false;
            body.linearVelocity = aimSource.forward * throwSpeed;
            if (throwSpin > 0f)
                body.angularVelocity = Random.insideUnitSphere * throwSpin;

            PhysicalEvents.Raise(PhysicalEventKind.Throw, obj, this);
        }

        private void EnableHeldColliders()
        {
            if (_heldColliders == null)
                return;
            foreach (var col in _heldColliders)
                if (col != null) col.enabled = true;
            _heldColliders = null;
        }

        private static float ComputePivotToBottom(Transform t, Collider[] colliders)
        {
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (var col in colliders)
            {
                if (col == null) continue;
                if (!hasBounds) { bounds = col.bounds; hasBounds = true; }
                else bounds.Encapsulate(col.bounds);
            }
            return hasBounds ? t.position.y - bounds.min.y : 0f;
        }

        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying && _held != null && _placeValid)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireCube(_placePosition, Vector3.one * 0.15f);
            }
        }
    }
}
