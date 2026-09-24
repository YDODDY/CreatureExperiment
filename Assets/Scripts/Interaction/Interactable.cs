using UnityEngine;

namespace CreatureExperiment.Interaction
{
    /// <summary>How an object was last thrown. Physical only - Strong is "thrown hard at an aimed spot", not an attack.</summary>
    public enum ThrowMode
    {
        None,
        Normal,
        Strong
    }

    /// <summary>
    /// Marker for objects that can be picked up and carried (by the player, or by a creature) and
    /// placed or thrown. It only carries the data the interaction system needs right now, plus a
    /// single-slot <see cref="Holder"/> so two carriers cannot hold the same object at once.
    /// Per-object "use" behaviour is intentionally left out until it is needed.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class Interactable : MonoBehaviour, IFocusTarget
    {
        [Header("Display")]
        [Tooltip("Name shown to the player when this object is focused. Falls back to the GameObject name.")]
        [SerializeField] private string displayName;

        [Header("Identity")]
        [Tooltip("Stable identifier for what kind of item this is (e.g. \"EmptyCan\"). Kept in records that outlive the object, like a GarbageDump's discard list. Falls back to the GameObject name.")]
        [SerializeField] private string itemId;
        [Tooltip("Can be thrown away in a GarbageDump. Off for items that must not disappear (work items).")]
        [SerializeField] private bool discardable;

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

        /// <summary>Change the focus name at runtime (e.g. a work item that turns into "파손품").</summary>
        public void SetDisplayName(string value) => displayName = value;

        /// <summary>What kind of item this is - survives as plain data after the object itself is gone.</summary>
        public string ItemId => string.IsNullOrEmpty(itemId) ? gameObject.name : itemId;

        /// <summary>True if this item may be thrown away in a GarbageDump.</summary>
        public bool IsDiscardable => discardable;

        // --- IFocusTarget: the Focus feedback (outline + name) treats an Interactable and a
        //     non-pickup FocusableProp the same way. SetFocused(bool) below is the third member.
        string IFocusTarget.FocusName => DisplayName;
        Transform IFocusTarget.FocusTransform => transform;

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
        /// True while this object is physically in flight from a Player throw (set by
        /// <c>PlayerInteractor.Throw</c>, cleared on HIT or on being picked up again mid-flight -
        /// see <see cref="SetInFlight"/>). Distinct from <see cref="IsHeld"/>: nobody is carrying it,
        /// it is just not a normal resting world object yet. A creature's own throw does not set this
        /// (out of scope for now). Not a claim/ownership - just a physical state flag, same shape as
        /// <see cref="SetFocused"/> below - so it stays free for a future Catch action to also read.
        /// </summary>
        public bool IsInFlight { get; private set; }

        /// <summary>
        /// The most recent throw of this object (mode, who threw it, when), recorded by the thrower. Cleared
        /// when someone grabs it, so it only ever describes the flight / landing since that throw - enough to
        /// ask later "was this impact the result of a Player Strong Throw?". Not damage, not intent.
        /// </summary>
        public ThrowMode LastThrowMode { get; private set; }
        public Object LastThrower { get; private set; }
        public float LastThrowTime { get; private set; } = float.NegativeInfinity;

        public void RecordThrow(ThrowMode mode, Object thrower)
        {
            LastThrowMode = mode;
            LastThrower = thrower;
            LastThrowTime = Time.time;
        }

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
            LastThrowMode = ThrowMode.None;
            LastThrower = null;
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

        /// <summary>Set <see cref="IsInFlight"/>. Called by <c>PlayerInteractor.Throw</c> (true), and by whatever resolves the flight - a mid-flight <c>PlayerInteractor.Pickup</c> or a confirmed HIT (false) right now; a future "came to rest" check is not built yet.</summary>
        public void SetInFlight(bool inFlight)
        {
            IsInFlight = inFlight;
        }
    }
}
