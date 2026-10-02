using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A store doorway that won't let the player walk out with unpaid goods. Sits in the door opening; its forward
    /// (+Z) points OUT of the store, local X runs along the opening. Nothing is stored about the player - whether the
    /// way is shut is worked out every frame from the goods themselves (<see cref="CheckoutCounter.PlayerHasUnpaid"/>:
    /// every inventory slot, this store's <see cref="StoreProduct"/>s with paid == false). Paying makes the goods
    /// paid, and the doorway is simply open again.
    ///
    /// Blocking: an invisible solid box (built at runtime, so the saved scene / NavMesh bake never see it) fills the
    /// opening while the player carries unpaid goods and is inside. The CharacterController stops against it, so
    /// running, diagonals or pushing the door again can't get through. It switches on only once the player's capsule
    /// is fully inside (never around them) and off as soon as nothing is unpaid - or the player is outside (someone
    /// who got unpaid goods outside can still come in). Walking back into the store is always free.
    ///
    /// Warning: pushing outward at the blocked opening (door open) makes the cashier speak once
    /// (<see cref="CheckoutCounter.WarnUnpaidExit"/>, closed with E). It re-arms only after stepping back
    /// <see cref="rearmDistance"/> into the store, so it never reopens every frame - but the block itself never
    /// depends on having been warned.
    /// </summary>
    [DefaultExecutionOrder(-50)] // before PlayerMovement moves the CharacterController this frame
    public class StoreExitGate : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The store's checkout - its storeId decides which goods count, its cashier gives the warning.")]
        [SerializeField] private CheckoutCounter checkout;
        [Tooltip("The door in this opening - the warning only fires while it is open (a closed door already blocks).")]
        [SerializeField] private SwingDoor door;
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private PlayerInteractor player;

        [Header("Opening (metres, this object's local axes)")]
        [Tooltip("Width (local X) and height (local Y) of the barrier; centred on this object.")]
        [SerializeField] private Vector2 openingSize = new Vector2(1.8f, 2.2f);
        [Tooltip("Barrier thickness along local Z.")]
        [SerializeField] private float thickness = 0.16f;

        [Header("Warning")]
        [Tooltip("How close (behind the barrier's inner face, beyond the capsule radius) counts as 'at the door'.")]
        [SerializeField] private float warnDepth = 0.6f;
        [Tooltip("Stepping this far back into the store allows the next warning.")]
        [SerializeField] private float rearmDistance = 1.6f;
        [Tooltip("Move input pointing out of the store at least this much (dot) counts as trying to leave.")]
        [SerializeField] private float pushDot = 0.3f;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private bool blocking;
        [SerializeField] private bool warningArmed = true;

        private BoxCollider _barrier;
        private CharacterController _body;
        private InputAction _move;

        public bool Blocking => blocking;

        private System.Func<bool> _storyHold;
        private System.Func<bool> _storyWarn;

        /// <summary>
        /// A story may also keep the player in (e.g. "buy lunch first"): while <paramref name="holdWhile"/> returns true the
        /// doorway blocks exactly as for unpaid goods, and pushing out calls <paramref name="warn"/> (returns whether a
        /// warning started). Unpaid goods always come first - the cashier's block and warning win. Pass nulls to clear.
        /// </summary>
        public void SetStoryHold(System.Func<bool> holdWhile, System.Func<bool> warn)
        {
            _storyHold = holdWhile;
            _storyWarn = warn;
        }

        private void Awake()
        {
            if (player == null)
                player = FindFirstObjectByType<PlayerInteractor>();
            if (player != null)
            {
                _body = player.GetComponent<CharacterController>();
                var movement = player.GetComponent<PlayerMovement>();
                _move = movement != null && movement.InputActions != null
                    ? movement.InputActions.FindActionMap("Player", throwIfNotFound: false)?.FindAction("Move", throwIfNotFound: false)
                    : null;
            }

            var go = new GameObject("ExitBarrier (runtime)");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            _barrier = go.AddComponent<BoxCollider>();
            _barrier.size = new Vector3(openingSize.x, openingSize.y, thickness);
            _barrier.enabled = false;
        }

        private void OnDisable()
        {
            blocking = false;
            if (_barrier != null)
                _barrier.enabled = false;
        }

        private void Update()
        {
            if (player == null || checkout == null)
                return;

            Vector3 local = transform.InverseTransformPoint(player.transform.position);
            float radius = _body != null ? _body.radius + _body.skinWidth : 0.5f;
            float innerFace = -(thickness * 0.5f + radius); // capsule centre just touching the barrier from inside

            bool unpaid = checkout.PlayerHasUnpaid();
            bool hold = unpaid || (_storyHold != null && _storyHold());
            if (!hold || local.z > 0f)
                blocking = false;                       // nothing to stop, or already outside: always open
            else if (local.z < innerFace - 0.02f)
                blocking = true;                        // fully inside - safe to close without touching the capsule
            // else: straddling the opening - keep the current state (never switch on around the player)
            _barrier.enabled = blocking;

            UpdateWarning(local, innerFace, unpaid);
        }

        private void UpdateWarning(Vector3 local, float innerFace, bool unpaid)
        {
            bool nearOpening = Mathf.Abs(local.x) < openingSize.x * 0.5f + 0.2f;
            if (!blocking || local.z < innerFace - rearmDistance || Mathf.Abs(local.x) > openingSize.x * 0.5f + 0.8f)
                warningArmed = true;

            if (!blocking || !warningArmed || checkout.InConversation)
                return;
            if (!nearOpening || local.z < innerFace - warnDepth)
                return;
            if (door != null && !door.IsOpen && !door.IsMoving)
                return;
            if (!PushingOut())
                return;

            bool started = unpaid ? checkout.WarnUnpaidExit() : _storyWarn != null && _storyWarn();
            if (started)
                warningArmed = false;
        }

        // The player's move input (not velocity - they are standing against the barrier) points out of the store.
        private bool PushingOut()
        {
            if (_move == null || !_move.enabled)
                return false;
            Vector2 input = _move.ReadValue<Vector2>();
            if (input.sqrMagnitude < 0.01f)
                return false;
            Transform t = player.transform;
            Vector3 wish = (t.right * input.x + t.forward * input.y).normalized;
            return Vector3.Dot(wish, transform.forward) > pushDot;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = blocking ? new Color(1f, 0.2f, 0.2f, 0.5f) : new Color(1f, 0.8f, 0.2f, 0.35f);
            Gizmos.DrawCube(Vector3.zero, new Vector3(openingSize.x, openingSize.y, thickness));
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.6f); // outward
        }
    }
}
