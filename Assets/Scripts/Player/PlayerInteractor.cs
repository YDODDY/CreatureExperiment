using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Interaction;
using CreatureExperiment.DailyLife;

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
    /// This is also the single owner of the Interact key. One press runs at most one action, picked
    /// from what the centre ray actually hits (see <see cref="ResolveAim"/>), whether or not an item
    /// is held: World Use (<see cref="IUsable"/>, found via <see cref="PlayerActivator"/>) &gt; hand
    /// the held item to an <see cref="IHeldItemReceiver"/> &gt; Pickup (or Swap while holding) &gt;
    /// Place. <see cref="PlayerActivator"/> no longer reads Interact itself while this is present.
    ///
    /// Strong Throw (Right Click, the "StrongThrow" action) is a second, separate release: a fast, flat
    /// throw at whatever the centre ray points at (see <see cref="StrongThrow"/>). F stays the soft toss.
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
        [Tooltip("Resolves World Use (IUsable) targets and their reach. Found on this GameObject if empty.")]
        [SerializeField] private PlayerActivator activator;

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

        [Header("Strong throw")]
        [Tooltip("Launch speed of a Strong Throw (Right Click).")]
        [SerializeField] private float strongThrowSpeed = 16f;
        [Tooltip("How far the centre ray looks for the aim point; with no hit, the point this far along the camera forward is used.")]
        [SerializeField] private float strongAimRange = 30f;
        [Tooltip("Gravity drop is compensated in the launch direction only up to this distance, so near and mid targets are hit without lobbing far ones.")]
        [SerializeField] private float strongDropCompensationRange = 10f;
        [Tooltip("Small random spin on a Strong Throw (lower than F, so the object flies cleanly). 0 = none.")]
        [SerializeField] private float strongThrowSpin = 0.5f;

        [Header("Safe release")]
        [Tooltip("How long a thrown item ignores the player's own capsule, so a release pulled in close to the body can't bounce off it.")]
        [SerializeField] private float throwIgnoreBodyTime = 0.3f;
        [Tooltip("How far a Place may be slid back toward the player when the item would stick into a wall / door at the aimed spot (m).")]
        [SerializeField] private float placeNudgeMax = 0.4f;

        private InputAction _interactAction;
        private InputAction _throwAction;
        private InputAction _strongThrowAction;

        private IFocusTarget _focus;
        private string _focusLabelOverride;
        private Interactable _held;
        private Collider[] _heldColliders;
        private float _heldPivotToBottom;
        // Shape of the held item, measured at pickup while its colliders were still on (they are off in the hand):
        // collider-bounds centre in the item's local space, and the world half-extents.
        private Vector3 _heldLocalCenter;
        private Vector3 _heldExtents;
        private CharacterController _ownBody;
        private readonly Collider[] _overlapBuffer = new Collider[16];

        // Per-frame aim resolution - what one Interact press would do right now (at most one is set).
        private IUsable _aimUsable;
        private IHeldItemReceiver _aimReceiver;
        private Interactable _aimPickup;
        private bool _aimRejected;          // aimed at a receiver that refuses the held item - the press does nothing
        private string _aimLabelOverride;   // label to show instead of the focus's own FocusName (a rejection prompt)

        // World Use that answers Interact when the aim resolves to no action at all (see SetFallbackUse).
        private IUsable _fallbackUse;

        // Per-frame placement solution while carrying.
        private bool _placeValid;
        private Vector3 _placePosition;
        private Quaternion _placeRotation;

        /// <summary>True while the player is carrying an item. Holding does not block World Use - it only changes what an Interact press with no target does (Place instead of nothing).</summary>
        public bool IsHolding => _held != null;

        /// <summary>The item currently carried, or null. Read-only - for HUDs that describe the held item.</summary>
        public Interactable HeldItem => _held;

        private void Awake()
        {
            var playerMap = inputActions.FindActionMap("Player", throwIfNotFound: true);
            _interactAction = playerMap.FindAction("Interact", throwIfNotFound: true);
            _throwAction = playerMap.FindAction("Throw", throwIfNotFound: true);
            _strongThrowAction = playerMap.FindAction("StrongThrow", throwIfNotFound: false);

            if (aimSource == null && Camera.main != null)
                aimSource = Camera.main.transform;
            if (activator == null)
                activator = GetComponent<PlayerActivator>();
            _ownBody = GetComponent<CharacterController>();
        }

        private void OnEnable()
        {
            _interactAction?.Enable();
            _throwAction?.Enable();
            _strongThrowAction?.Enable();
        }

        private void OnDisable()
        {
            _interactAction?.Disable();
            _throwAction?.Disable();
            _strongThrowAction?.Disable();
            SetFocus(null);
        }

        /// <summary>
        /// Register a World Use that answers Interact whenever the aim resolves to no action - e.g. the
        /// chair the player is sitting on ("일어서기"), which the player usually isn't looking at. Anything
        /// actually aimed at still wins; the fallback only comes before Place. Its focus (label) is shown
        /// while it is the answer. Pass null to clear.
        /// </summary>
        public void SetFallbackUse(IUsable usable) => _fallbackUse = usable;

        /// <summary>
        /// Put <paramref name="item"/> - something that was just taken out of a container (an egg from its
        /// carton) - straight into the empty hand, exactly like a pickup. False if the hand is already full
        /// or the item can't be claimed.
        /// </summary>
        public bool TryHoldNew(Interactable item)
        {
            if (_held != null || item == null)
                return false;
            return Pickup(item);
        }

        private void Update()
        {
            // A held tool in the middle of its Left Click action (tape being drawn): no E / F / Right Click
            // until it ends, so the roll can't be dropped, swapped or thrown mid-strip.
            if (_held != null && _held.TryGetComponent(out IHeldPrimaryAction primary) && primary.PrimaryActive)
            {
                SetFocus(null);
                return;
            }

            IFocusTarget aimFocus = ResolveAim();
            if (_fallbackUse as Object != null && _aimUsable == null && _aimReceiver == null && !_aimRejected && _aimPickup == null)
            {
                _aimUsable = _fallbackUse;
                aimFocus = _fallbackUse as IFocusTarget ?? aimFocus;
            }
            SetFocus(aimFocus, _aimLabelOverride);

            if (_held != null)
            {
                UpdatePlacementSolution();
                if (_throwAction.WasPressedThisFrame())
                {
                    Throw();
                    return;
                }
                if (_strongThrowAction != null && _strongThrowAction.WasPressedThisFrame())
                {
                    StrongThrow();
                    return;
                }
            }

            if (!_interactAction.WasPressedThisFrame())
                return;

            // One press, one action - the first that applies, in this order.
            if (_aimUsable != null)
                _aimUsable.Use();
            else if (_aimReceiver != null)
                HandOver(_aimReceiver);
            else if (_aimRejected)
                return; // consumed: a refusing receiver is still the target, so no Place fallback
            else if (_aimPickup != null)
            {
                if (_held != null) Swap(_aimPickup);
                else Pickup(_aimPickup);
            }
            else if (_held != null && _placeValid)
                Place();
        }

        /// <summary>
        /// The single source of truth for "what is the player pointing at, in reach, and what would
        /// Interact do with it". One ray; only the object it actually hits counts. Sets at most one of
        /// <see cref="_aimUsable"/> / <see cref="_aimReceiver"/> / <see cref="_aimPickup"/>, in priority
        /// order, and returns the focus (outline + label) that goes with it:
        /// 1. World Use - an <see cref="IUsable"/> within the activator's reach (door, lid, bed, terminal),
        ///    held item or not. Its own gameplay conditions stay inside its Use().
        /// 2. A receiver, while holding. It occupies the aim either way: if it accepts the held item the
        ///    press hands it over ("버리기"); if not, the press does nothing and its rejection prompt (if
        ///    any, e.g. "버릴 수 없습니다") is shown - it never falls back to Place.
        /// 3. A free <see cref="Interactable"/> - Pickup, or Swap while holding.
        /// With empty hands a plain <see cref="IFocusTarget"/> with no action still gets its label.
        /// While holding, no match means the press falls back to Place.
        /// </summary>
        private IFocusTarget ResolveAim()
        {
            _aimUsable = null;
            _aimReceiver = null;
            _aimPickup = null;
            _aimRejected = false;
            _aimLabelOverride = null;

            float useRange = activator != null ? activator.UseRange : 0f;
            var ray = new Ray(aimSource.position, aimSource.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Max(pickupRange, useRange), ~0, QueryTriggerInteraction.Ignore))
                return null;

            Collider col = hit.collider;
            IFocusTarget focus = col.GetComponentInParent<IFocusTarget>();

            if (activator != null && activator.TryGetUsable(hit, out IUsable usable))
            {
                _aimUsable = usable;
                return focus;
            }

            bool inMask = InMask(pickupMask, col.gameObject.layer);

            // An optional receiver (IOptionalHeldItemReceiver) that does not handle the held item is just
            // a plain object here - pickup / swap / place go on as usual. Optional receivers may also
            // reach a little further (e.g. a ceiling for a sticker), never beyond the ray.
            // A dedicated optional receiver (pan, meal plate) keeps the aim for every held item: one it does not
            // handle is refused below - never swapped with, never placed on.
            var receiver = col.GetComponentInParent<IHeldItemReceiver>();
            if (receiver is IOptionalHeldItemReceiver optional && !optional.AppliesTo(_held) && !(optional.IsDedicated && _held != null))
                receiver = null;
            if (_held != null && receiver as Object != null && inMask)
            {
                float reach = receiver is IOptionalHeldItemReceiver opt ? Mathf.Max(pickupRange, opt.MaxReach) : pickupRange;
                if (hit.distance <= reach)
                {
                    if (receiver.CanReceive(_held))
                    {
                        _aimReceiver = receiver;
                        return receiver;
                    }
                    _aimRejected = true;
                    _aimLabelOverride = receiver.GetRejectPrompt(_held);
                    return _aimLabelOverride != null ? receiver : null;
                }
            }

            if (hit.distance > pickupRange || !inMask)
                return null;

            // An object the creature is already holding is not a focus / pickup candidate.
            var item = col.GetComponentInParent<Interactable>();
            if (item != null && !item.IsHeld)
            {
                _aimPickup = item;
                return item;
            }

            // Anything else: a label with no action (empty hands only). A receiver that will not
            // take the item is not shown at all.
            if (_held == null && item == null && !(focus is IHeldItemReceiver))
                return focus;
            return null;
        }

        private static bool InMask(LayerMask mask, int layer) => (mask.value & (1 << layer)) != 0;

        /// <param name="labelOverride">Text to show instead of <paramref name="next"/>'s own FocusName (a rejection prompt), or null.</param>
        private void SetFocus(IFocusTarget next, string labelOverride = null)
        {
            if (ReferenceEquals(next, _focus) && labelOverride == _focusLabelOverride)
                return;

            // `as Object` so the null test is Unity-lifetime-aware (an IFocusTarget is always a
            // MonoBehaviour); never call SetFocused on a destroyed object.
            if (!ReferenceEquals(next, _focus))
            {
                if (_focus as Object != null)
                    _focus.SetFocused(false);
                _focus = next;
                if (_focus as Object != null)
                    _focus.SetFocused(true);
            }
            _focusLabelOverride = labelOverride;

            if (_focus as Object != null)
            {
                if (focusLabel != null) focusLabel.Show(_focus, labelOverride);
            }
            else if (focusLabel != null)
            {
                focusLabel.Hide();
            }
        }

        private bool Pickup(Interactable interactable)
        {
            // Claim it. Fails if the creature already holds it - a plain pickup never takes.
            if (!interactable.TryGrab(this))
                return false;

            // A mid-flight catch ends the flight immediately - it is held now, not flying.
            interactable.SetInFlight(false);

            SetFocus(null);

            _held = interactable;
            _heldColliders = _held.GetComponentsInChildren<Collider>();

            // Cache how far the pivot sits above the object's lowest point, so we can
            // rest it flush on the ground later, and its size for a safe release. Colliders are still enabled here.
            _heldPivotToBottom = ComputePivotToBottom(_held.transform, _heldColliders);
            if (TryGetBounds(_heldColliders, out Bounds heldBounds))
            {
                _heldLocalCenter = _held.transform.InverseTransformPoint(heldBounds.center);
                _heldExtents = heldBounds.extents;
            }
            else
            {
                _heldLocalCenter = Vector3.zero;
                _heldExtents = Vector3.one * 0.05f;
            }

            foreach (var col in _heldColliders)
                if (col != null) col.enabled = false;

            var body = _held.Body;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;

            // Keep the item's own world size in the hand (it may come off a scaled plate / pan).
            Vector3 worldScale = _held.transform.lossyScale;
            _held.transform.SetParent(holdAnchor, worldPositionStays: false);
            _held.transform.localPosition = _held.HoldPositionOffset;
            _held.transform.localRotation = _held.HoldRotationOffset;
            Vector3 anchorScale = holdAnchor.lossyScale;
            _held.transform.localScale = new Vector3(worldScale.x / anchorScale.x, worldScale.y / anchorScale.y, worldScale.z / anchorScale.z);
            return true;
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

            // A receiver (GarbageDump) is not a shelf: an item goes into it via HandOver or not at all.
            // An optional receiver that does not handle the held item (a floor that only takes stickers)
            // is an ordinary surface.
            var surfaceReceiver = hit.collider.GetComponentInParent<IHeldItemReceiver>();
            if (surfaceReceiver as Object != null
                && !(surfaceReceiver is IOptionalHeldItemReceiver optional && !optional.AppliesTo(_held) && !optional.IsDedicated))
                return;

            _placePosition = hit.point + Vector3.up * _heldPivotToBottom;
            _placeRotation = Quaternion.Euler(0f, aimSource.eulerAngles.y, 0f);
            _placeValid = PlaceSpotClear(_placePosition, hit.collider) || NudgePlaceTowardPlayer(hit);
        }

        /// <summary>
        /// The item set down at the aimed spot must not stick into a wall / door / fridge beside it (the solver
        /// would shove it out - or through). Slide the spot back toward the player along the same kind of flat
        /// surface, a few cm at a time, up to <see cref="placeNudgeMax"/>. No clear spot = no Place.
        /// </summary>
        private bool NudgePlaceTowardPlayer(RaycastHit surface)
        {
            Vector3 toPlayer = transform.position - surface.point;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.0001f)
                return false;
            toPlayer.Normalize();

            const float step = 0.03f;
            for (float d = step; d <= placeNudgeMax + 0.0001f; d += step)
            {
                // Re-find the surface under the slid spot (still flat, still not a receiver).
                Vector3 above = surface.point + toPlayer * d + Vector3.up * 0.3f;
                if (!Physics.Raycast(above, Vector3.down, out RaycastHit under, 0.6f, placeMask, QueryTriggerInteraction.Ignore))
                    continue;
                if (Vector3.Angle(under.normal, Vector3.up) > placeMaxSlope)
                    continue;
                if (under.collider.GetComponentInParent<IHeldItemReceiver>() as Object != null)
                    continue;
                Vector3 candidate = under.point + Vector3.up * _heldPivotToBottom;
                if (PlaceSpotClear(candidate, under.collider))
                {
                    _placePosition = candidate;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Nothing solid in the item's footprint at <paramref name="pivotPosition"/>: a conservative upright box -
        /// the item's height, and its widest horizontal half-extent on both X and Z (a place turns it to the camera
        /// yaw). The bottom is lifted 1 cm so the surface it rests on doesn't count; the player's capsule doesn't either.
        /// </summary>
        private bool PlaceSpotClear(Vector3 pivotPosition, Collider surface)
        {
            float half = Mathf.Max(_heldExtents.x, _heldExtents.z);
            Vector3 bottom = pivotPosition - Vector3.up * _heldPivotToBottom;
            Vector3 halfExtents = new Vector3(half, Mathf.Max(_heldExtents.y - 0.01f, 0.005f), half);
            Vector3 center = bottom + Vector3.up * (0.01f + halfExtents.y);
            int n = Physics.OverlapBoxNonAlloc(center, halfExtents, _overlapBuffer, Quaternion.identity, placeMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = _overlapBuffer[i];
                if (c == surface || IsOwnBody(c) || c.attachedRigidbody != null)
                    continue; // loose items nearby are not walls - they just get nudged, as before
                return false;
            }
            return true;
        }

        /// <summary>
        /// Before a throw lets go: the item in the hand sits ~0.65 m in front of the camera - past the face of a
        /// wall / door the player stands against (the capsule stops ~0.22 m from it), sometimes past the whole
        /// wall. Pull the item back along the camera-to-item line until the camera can see its centre and nothing
        /// solid overlaps it (at worst to the camera itself, which is inside the player's capsule). The direction
        /// and speed of the throw are not touched.
        /// </summary>
        private void MoveToSafeRelease(Interactable obj)
        {
            Vector3 eye = aimSource.position;
            Vector3 center = obj.transform.TransformPoint(_heldLocalCenter);
            Vector3 toCenter = center - eye;
            float dist = toCenter.magnitude;
            if (dist < 0.0001f)
                return;
            Vector3 dir = toCenter / dist;
            float radius = Mathf.Clamp(Mathf.Max(_heldExtents.x, Mathf.Max(_heldExtents.y, _heldExtents.z)), 0.02f, 0.3f);

            // A wall between the eye and the item: the item is inside it or already through it.
            float d = dist;
            if (Physics.Raycast(eye, dir, out RaycastHit block, dist, ~0, QueryTriggerInteraction.Ignore)
                && !IsOwnBody(block.collider) && block.collider.attachedRigidbody == null)
                d = Mathf.Max(0f, block.distance - radius);

            float step = Mathf.Max(d / 8f, 0.01f);
            while (d > 0f && SphereBlocked(eye + dir * d, radius))
                d = d > step ? d - step : 0f;

            if (d < dist - 0.0001f)
                obj.transform.position += eye + dir * d - center;
        }

        private bool SphereBlocked(Vector3 center, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(center, radius, _overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!IsOwnBody(_overlapBuffer[i]) && _overlapBuffer[i].attachedRigidbody == null)
                    return true; // only structure counts - walls, doors, floors, furniture
            return false;
        }

        private bool IsOwnBody(Collider c) => c != null && (c == _ownBody || c.transform.IsChildOf(transform));

        /// <summary>A just-thrown item passes through the thrower's capsule for a moment (it may have been pulled back into it).</summary>
        private void IgnoreOwnBodyBriefly(Collider[] colliders)
        {
            if (_ownBody == null || colliders == null || throwIgnoreBodyTime <= 0f)
                return;
            foreach (var col in colliders)
                if (col != null && col.enabled)
                    Physics.IgnoreCollision(col, _ownBody, true);
            StartCoroutine(RestoreOwnBody(colliders));
        }

        private IEnumerator RestoreOwnBody(Collider[] colliders)
        {
            yield return new WaitForSeconds(throwIgnoreBodyTime);
            if (_ownBody == null)
                yield break;
            // Every collider, enabled or not: an ignore survives disable / enable, and an item caught again within
            // the window has its colliders off in the hand - skipping them left it passing through the player for good.
            foreach (var col in colliders)
                if (col != null)
                    Physics.IgnoreCollision(col, _ownBody, false);
        }

        private void Place()
        {
            Interactable obj = _held;
            Collider[] cols = _heldColliders;
            _held = null;
            _heldColliders = null;
            PutDown(obj, cols, _placePosition, _placeRotation, sleep: true);
        }

        /// <summary>
        /// Swap: the held item is set down where <paramref name="target"/> is resting, and
        /// <paramref name="target"/> is picked up. The target is claimed first, so if it cannot be
        /// taken nothing changes. It leaves the world (colliders off) before the old item is put in its
        /// spot, so the two never overlap, and that spot is where an object was already resting in reach
        /// - not inside the player. The old item is a normal Place (event included), never destroyed.
        /// </summary>
        private void Swap(Interactable target)
        {
            if (!target.TryGrab(this))
                return;

            Vector3 spot = RestingSpot(target);
            Quaternion rot = Quaternion.Euler(0f, aimSource.eulerAngles.y, 0f);

            Interactable previous = _held;
            Collider[] previousCols = _heldColliders;
            float previousPivotToBottom = _heldPivotToBottom;
            _held = null;
            _heldColliders = null;

            Pickup(target);
            // Not put to sleep: the target may have been caught mid-air, so let physics settle it.
            PutDown(previous, previousCols, spot + Vector3.up * previousPivotToBottom, rot, sleep: false);
        }

        /// <summary>Bottom-centre of an object's colliders - the point it is resting on.</summary>
        private static Vector3 RestingSpot(Interactable item)
        {
            var colliders = item.GetComponentsInChildren<Collider>();
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (var col in colliders)
            {
                if (col == null || !col.enabled) continue;
                if (!hasBounds) { bounds = col.bounds; hasBounds = true; }
                else bounds.Encapsulate(col.bounds);
            }
            if (!hasBounds)
                return item.transform.position;
            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        /// <summary>Release <paramref name="obj"/> (no longer ours) into the world at rest at the given pose.</summary>
        private void PutDown(Interactable obj, Collider[] colliders, Vector3 position, Quaternion rotation, bool sleep)
        {
            obj.Release(this);

            obj.transform.SetParent(null, worldPositionStays: true);
            obj.transform.SetPositionAndRotation(position, rotation);
            EnableColliders(colliders);

            var body = obj.Body;
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            if (sleep)
                body.Sleep();

            PhysicalEvents.Raise(PhysicalEventKind.Place, obj, this);
        }

        private void Throw()
        {
            Interactable obj = _held;
            Collider[] cols = _heldColliders;
            _held = null;
            obj.Release(this);
            obj.SetInFlight(true);

            obj.transform.SetParent(null, worldPositionStays: true);
            MoveToSafeRelease(obj);
            EnableColliders(cols);
            IgnoreOwnBodyBriefly(cols);
            _heldColliders = null;

            var body = obj.Body;
            body.isKinematic = false;
            body.linearVelocity = aimSource.forward * throwSpeed;
            if (throwSpin > 0f)
                body.angularVelocity = Random.insideUnitSphere * throwSpin;

            obj.RecordThrow(ThrowMode.Normal, this);
            PhysicalEvents.Raise(PhysicalEventKind.Throw, obj, this);
        }

        /// <summary>
        /// Throw the held item hard at the aim point: the centre ray's hit within
        /// <see cref="strongAimRange"/>, else the point that far along the camera forward. The launch goes
        /// from where the item actually is (the hand, not the camera) toward that point, so near targets
        /// are not missed by the hand offset, with the gravity drop over the (capped) distance folded into
        /// the launch angle. Set once at release - no homing, no correction in flight; plain Rigidbody
        /// physics from then on. Same Throw event as F; the Strong mode is recorded on the item.
        /// </summary>
        private void StrongThrow()
        {
            Interactable obj = _held;
            var ray = new Ray(aimSource.position, aimSource.forward);
            Vector3 target = Physics.Raycast(ray, out RaycastHit hit, strongAimRange, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point
                : ray.GetPoint(strongAimRange);

            Collider[] cols = _heldColliders;
            _held = null;
            obj.Release(this);
            obj.SetInFlight(true);

            obj.transform.SetParent(null, worldPositionStays: true);
            MoveToSafeRelease(obj);
            EnableColliders(cols);
            IgnoreOwnBodyBriefly(cols);
            _heldColliders = null;

            var body = obj.Body;
            Vector3 from = obj.transform.TransformPoint(body.centerOfMass); // transform, not the body pose: it was just unparented
            Vector3 toTarget = target - from;
            float compensated = Mathf.Min(toTarget.magnitude, strongDropCompensationRange);
            float flightTime = compensated / strongThrowSpeed;
            Vector3 aimAt = target + Vector3.up * (0.5f * -Physics.gravity.y * flightTime * flightTime);
            Vector3 dir = (aimAt - from).sqrMagnitude > 0.0001f ? (aimAt - from).normalized : aimSource.forward;

            body.isKinematic = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // fast and small: don't pass through thin walls
            body.linearVelocity = dir * strongThrowSpeed;
            body.angularVelocity = strongThrowSpin > 0f ? Random.insideUnitSphere * strongThrowSpin : Vector3.zero;

            obj.RecordThrow(ThrowMode.Strong, this);
            PhysicalEvents.Raise(PhysicalEventKind.Throw, obj, this);
        }

        /// <summary>
        /// Give the held item to <paramref name="receiver"/> (throw it into a GarbageDump). The hold is
        /// released and our references cleared first, so nothing here points at the object once the
        /// receiver removes it from the world. No PhysicalEvent - this is not a place / throw.
        /// </summary>
        private void HandOver(IHeldItemReceiver receiver)
        {
            Interactable obj = _held;
            _held = null;
            _heldColliders = null; // left disabled - the object is leaving the world
            obj.Release(this);
            obj.transform.SetParent(null, worldPositionStays: true);

            SetFocus(null);
            receiver.Receive(obj);
        }

        private static void EnableColliders(Collider[] colliders)
        {
            if (colliders == null)
                return;
            foreach (var col in colliders)
                if (col != null) col.enabled = true;
        }

        private static bool TryGetBounds(Collider[] colliders, out Bounds bounds)
        {
            bool hasBounds = false;
            bounds = default;
            foreach (var col in colliders)
            {
                if (col == null || !col.enabled) continue;
                if (!hasBounds) { bounds = col.bounds; hasBounds = true; }
                else bounds.Encapsulate(col.bounds);
            }
            return hasBounds;
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
