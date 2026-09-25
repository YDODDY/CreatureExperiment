using System;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A thrown knife that meets a surface hard enough (<see cref="minImpactSpeed"/>) and head-on enough
    /// (<see cref="minHeadOn"/>) sticks in it, tip a little into the surface, held still (kinematic).
    ///
    /// - Fixed surfaces (a collider with no Rigidbody - wall, floor, furniture, a door leaf): Strong Throw only
    ///   (it flies tip first). The knife is attached with <see cref="SurfaceMount"/>, so it swings along with a door.
    /// - A bread (a <see cref="FoodItem"/> of kind Bread): F or Strong Throw alike, first contact of the throw only, and a
    ///   plain gameplay rule - the blade side touched it = stick, only the handle side = bounce (see EvaluateBreadHit); no
    ///   speed / angle requirements (the wall rules above do not apply to bread). A coated knife first
    ///   spreads onto the bread (<see cref="KnifeCoating.TrySpreadOnThrownHit"/>), then the stick is judged. On a stick
    ///   the bread keeps its pre-impact pose (no visible shove / bounce).
    ///   The two stay separate objects: the knife is NOT parented to the bread (a pickup / eat of the bread must never
    ///   take the knife along). It stays at scene level and follows the bread's pose every frame, collisions with the
    ///   bread ignored. While someone holds the bread the knife rides along with its colliders off (it must not get in
    ///   the way of the holder's aim). If the bread is gone (eaten) the knife is simply let go and falls.
    ///
    /// Either way it stays a normal <see cref="Interactable"/>: E on the knife itself picks it straight out (the pickup
    /// re-parents it to the hand, which ends the attachment) and it can be thrown again. Other items and the creature
    /// (Rigidbody, not bread) are never stuck into.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class KnifeStick : MonoBehaviour
    {
        /// <summary>Raised when the knife sticks: knife, the surface collider. Nothing listens yet.</summary>
        public static event Action<KnifeStick, Collider> Stuck;

        [Tooltip("Marker at the blade tip; pivot -> tip is the direction the knife flies.")]
        [SerializeField] private Transform tip;
        [SerializeField] private float minImpactSpeed = 8f;
        [Tooltip("How head-on the hit must be: dot of the flight direction and the surface's inward normal.")]
        [Range(0f, 1f)]
        [SerializeField] private float minHeadOn = 0.5f;
        [SerializeField] private float embedDepth = 0.03f;

        [Header("Bread (gameplay rule: blade side sticks, handle side bounces)")]
        [Tooltip("Knife-local Z (unscaled) where the handle ends and the blade begins. Measured: handle -0.168..-0.048, blade -0.048..+0.168 (Tip marker +0.168).")]
        [SerializeField] private float bladeStartLocalZ = -0.048f;
        [Tooltip("Leniency toward the blade (knife-local units): a contact up to this far on the handle side of the boundary still counts as blade.")]
        [SerializeField] private float bladeBias = 0.02f;
        [Tooltip("Diagnostics: log one throw of this knife end to end (THROW / HIT / ARM / SPREAD / STICK), at most a few lines per throw. Off by default.")]
        [SerializeField] private bool debugThrowTrace;

        private Interactable _item;
        private KnifeCoating _coating;
        private bool _armed;
        private readonly ContactPoint[] _contacts = new ContactPoint[8];
        private int _traceLines;
        private const int MaxTraceLinesPerThrow = 10;
        private Vector3 _looseScale = Vector3.one;

        // Stuck in a bread (a moving item) - followed, never parented.
        private bool _inItem;
        private Transform _host;
        private Interactable _hostItem;
        private Collider[] _hostColliders;
        private Collider[] _ownColliders;
        private Vector3 _hostLocalPos;
        private Quaternion _hostLocalRot;
        private bool _collidersHidden;

        public bool IsStuck { get; private set; }

        /// <summary>The bread this knife is stuck in, or null (loose, held, or stuck in a fixed surface).</summary>
        public Interactable StuckInItem => _inItem ? _hostItem : null;

        private Interactable Item => _item != null ? _item : (_item = GetComponent<Interactable>());

        private void Awake() => _coating = GetComponent<KnifeCoating>();
        private void OnEnable() => PhysicalEvents.Raised += OnPhysicalEvent;
        private void OnDisable() => PhysicalEvents.Raised -= OnPhysicalEvent;

        // Raised by the thrower right after the launch velocity is set. Any throw can stick (a bread takes F too);
        // only a Strong Throw turns the tip into the flight (no tumbling).
        private void OnPhysicalEvent(PhysicalEvent evt)
        {
            if (evt.Interactable != Item || evt.Kind != PhysicalEventKind.Throw)
                return;
            _armed = Item.LastThrowMode != ThrowMode.None;
            _traceLines = 0;
            Trace($"THROW mode={Item.LastThrowMode} armed={_armed} coating={(_coating != null ? _coating.CurrentSpread.ToString() : "-")}");
            if (!_armed || Item.LastThrowMode != ThrowMode.Strong)
                return;

            var body = Item.Body;
            Vector3 v = body.linearVelocity;
            if (v.sqrMagnitude < 0.01f)
                return;
            Quaternion rot = TipAlong(v.normalized);
            transform.rotation = rot;
            body.rotation = rot;
            body.angularVelocity = Vector3.zero;
        }

        private void FixedUpdate()
        {
            if (_inItem)
                FollowHost();
        }

        private void Update()
        {
            if (IsStuck && Item.IsHeld)
            {
                // Taken out: the hand already re-parented it; undo the scale compensation of a scaled mount.
                if (_inItem)
                    ReleaseFromItem(drop: false);
                IsStuck = false;
                transform.localScale = _looseScale;
            }
        }

        private void LateUpdate()
        {
            if (_inItem)
                FollowHost();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (debugThrowTrace && Item.LastThrowMode != ThrowMode.None)
            {
                var food = collision.collider.GetComponentInParent<FoodItem>();
                Trace($"HIT step={Time.fixedTime:0.000} obj={collision.gameObject.name} collider={collision.collider.name} " +
                      $"bread={(food != null && food.Kind == FoodKind.Bread)} armedBefore={_armed} v={Item.PreImpactVelocity.magnitude:0.00} " +
                      $"coating={(_coating != null ? _coating.CurrentSpread.ToString() : "-")}");
            }
            if (!_armed || collision.collider is CharacterController)
                return;
            _armed = false; // first real contact only - no sticking after a bounce
            Trace($"ARM off by {collision.gameObject.name}");

            if (Item.IsHeld || Item.LastThrowMode == ThrowMode.None)
                return;

            if (collision.rigidbody != null)
            {
                TryStickIntoBread(collision);
                return;
            }
            if (Item.LastThrowMode != ThrowMode.Strong)
                return; // fixed surfaces: Strong Throw only, as before
            if (!HeadOnHit(collision, out Vector3 dir, out ContactPoint contact))
                return;

            Pin(contact.point, dir);
            _looseScale = transform.localScale;
            SurfaceMount.Attach(transform, collision.collider);
            IsStuck = true;
            Stuck?.Invoke(this, collision.collider);
        }

        private void TryStickIntoBread(Collision collision)
        {
            var bread = collision.collider.GetComponentInParent<FoodItem>();
            if (bread == null || bread.Kind != FoodKind.Bread || bread.Interactable.IsHeld)
                return;

            // Topping first (a coated knife leaves its jam / butter on the bread), then the stick.
            if (_coating != null)
            {
                bool spread = _coating.TrySpreadOnThrownHit(collision);
                Trace($"SPREAD (from stick) {(spread ? "ok" : "no: " + _coating.LastSpreadResult)}");
            }

            string fail = EvaluateBreadHit(collision, out Vector3 dir, out Vector3 point, out string detail);
            Trace($"STICK {(fail == null ? "STUCK" : "BOUNCE (" + fail + ")")} {detail}");
            if (fail != null)
                return;

            // The solver has already answered this contact (the bread was shoved, the knife bounced). A stick means
            // the blade went in instead: give the bread back its pose / motion from before the step, then pin.
            Interactable breadItem = bread.Interactable;
            Rigidbody breadBody = breadItem.Body;
            if (!breadBody.isKinematic)
            {
                Vector3 shift = breadItem.PreImpactPosition - breadBody.position;
                breadItem.transform.SetPositionAndRotation(breadItem.PreImpactPosition, breadItem.PreImpactRotation);
                breadBody.linearVelocity = breadItem.PreImpactVelocity;
                breadBody.angularVelocity = Vector3.zero;
                point += shift;
            }

            Pin(point, dir);
            _looseScale = transform.localScale;

            _inItem = true;
            _hostItem = bread.Interactable;
            _host = _hostItem.transform;
            _hostColliders = _hostItem.GetComponentsInChildren<Collider>(true);
            _ownColliders = GetComponentsInChildren<Collider>(true);
            _hostLocalPos = _host.InverseTransformPoint(transform.position);
            _hostLocalRot = Quaternion.Inverse(_host.rotation) * transform.rotation;
            IgnoreHost(true);

            IsStuck = true;
            Stuck?.Invoke(this, collision.collider);
        }

        /// <summary>
        /// Knife into bread - a plain gameplay rule, no speed / angle / tip-distance tests: which end of the knife touched
        /// the bread? Every contact of this first collision is taken into knife-local space at the pose it had when the
        /// contact was made (the pre-impact snapshot - the body has already been moved by the solver) and sorted along
        /// the knife's long axis (local Z): at or beyond <see cref="bladeStartLocalZ"/> - <see cref="bladeBias"/> = blade,
        /// behind it = handle. Any blade contact = stick (a mixed / ambiguous hit counts as blade); only a hit where every
        /// contact is on the handle bounces. Contact order is not trusted. Null = stick, else why not.
        /// </summary>
        private string EvaluateBreadHit(Collision collision, out Vector3 dir, out Vector3 point, out string detail)
        {
            Vector3 v = Item.PreImpactVelocity;
            Vector3 blade = Item.PreImpactRotation * (TipLocal.sqrMagnitude > 0f ? TipLocal.normalized : Vector3.forward);
            dir = v.sqrMagnitude > 0.0001f ? v.normalized : blade; // how the knife is pinned in
            point = Vector3.zero;

            Quaternion toLocal = Quaternion.Inverse(Item.PreImpactRotation);
            float scale = Mathf.Abs(transform.lossyScale.z) > 0.0001f ? Mathf.Abs(transform.lossyScale.z) : 1f;
            float boundary = bladeStartLocalZ - bladeBias;

            int n = collision.GetContacts(_contacts);
            int bladeContacts = 0;
            float bestBladeZ = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float z = (toLocal * (_contacts[i].point - Item.PreImpactPosition)).z / scale;
                if (z < boundary)
                    continue;
                bladeContacts++;
                if (z > bestBladeZ) { bestBladeZ = z; point = _contacts[i].point; } // the one nearest the tip
            }
            detail = $"contacts={n} blade={bladeContacts} handle={n - bladeContacts} speed={v.magnitude:0.00}";

            if (n == 0) return "no contact";
            if (bladeContacts == 0) return "handle side first";
            return null;
        }

        private bool HeadOnHit(Collision collision, out Vector3 dir, out ContactPoint contact)
        {
            contact = collision.GetContact(0);
            dir = Vector3.zero;
            Vector3 v = Item.PreImpactVelocity;
            float speed = v.magnitude;
            if (speed < minImpactSpeed)
                return false;
            dir = v / speed;
            Vector3 normal = contact.normal;
            if (Vector3.Dot(normal, dir) > 0f)
                normal = -normal;
            return Vector3.Dot(dir, -normal) >= minHeadOn;
        }

        // Stop dead, tip a little into the surface along the flight.
        private void Pin(Vector3 point, Vector3 dir)
        {
            var body = Item.Body;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;

            Quaternion rot = TipAlong(dir);
            Vector3 tipOffset = rot * Vector3.Scale(TipLocal, transform.lossyScale);
            transform.SetPositionAndRotation(point + dir * embedDepth - tipOffset, rot);
        }

        // Ride the bread (not parented): same pose relative to it every frame; hidden colliders while it is held.
        private void FollowHost()
        {
            if (Item.IsHeld)
                return; // being pulled out this frame - Update ends the attachment
            if (_host == null || !_host.gameObject.activeInHierarchy)
            {
                ReleaseFromItem(drop: true); // the bread is gone (eaten): let go
                IsStuck = false;
                return;
            }

            transform.SetPositionAndRotation(_host.TransformPoint(_hostLocalPos), _host.rotation * _hostLocalRot);
            SetOwnColliders(HostInWorld());
            IgnoreHost(true);
        }

        // The bread is physically in the world: not held, and its colliders are on (a held pan / plate it sits on
        // switches them off with its own). Otherwise the knife only rides along visually.
        private bool HostInWorld()
        {
            if (_hostItem == null || _hostItem.IsHeld || _hostColliders == null)
                return false;
            foreach (var c in _hostColliders)
                if (c != null && c.enabled && c.gameObject.activeInHierarchy)
                    return true;
            return false;
        }

        private void ReleaseFromItem(bool drop)
        {
            IgnoreHost(false);
            if (_collidersHidden && !Item.IsHeld)
                SetOwnColliders(true); // (in the hand the interactor owns them and re-enables them on release)
            _collidersHidden = false;
            _inItem = false;
            _host = null;
            _hostItem = null;
            _hostColliders = null;
            if (drop && !Item.IsHeld)
            {
                var body = Item.Body;
                body.isKinematic = false;
                body.WakeUp();
            }
        }

        private void SetOwnColliders(bool on)
        {
            if (_collidersHidden == !on || _ownColliders == null)
                return;
            foreach (var c in _ownColliders)
                if (c != null) c.enabled = on;
            _collidersHidden = !on;
            if (on)
                IgnoreHost(true); // re-enabled colliders lost their ignore pairs
        }

        // Only live colliders can take the call; disabling either side drops the pair, so it is re-asserted.
        //
        // Un-ignoring is done for EVERY pair, enabled or not: an ignore survives a collider being disabled and
        // re-enabled (checked on this Unity version), and the knife is pulled out by a pickup that has already
        // switched its colliders off - skipping those left the pair ignored for good, so that knife could never
        // touch that bread again (no spread, no stick - it went straight through onto the table).
        private void IgnoreHost(bool ignore)
        {
            if (_ownColliders == null || _hostColliders == null)
                return;
            foreach (var own in _ownColliders)
            {
                if (own == null || (ignore && (!own.enabled || !own.gameObject.activeInHierarchy))) continue;
                foreach (var host in _hostColliders)
                    if (host != null && (!ignore || (host.enabled && host.gameObject.activeInHierarchy)))
                        Physics.IgnoreCollision(own, host, ignore);
            }
        }

        /// <summary>Diagnostics line (only with <see cref="debugThrowTrace"/> on, capped per throw).</summary>
        internal void Trace(string message)
        {
            if (!debugThrowTrace || _traceLines >= MaxTraceLinesPerThrow)
                return;
            _traceLines++;
            Debug.Log($"[KnifeTrace] {message}", this);
        }

        private Vector3 TipLocal => tip != null ? tip.localPosition : Vector3.forward * 0.14f;

        private Quaternion TipAlong(Vector3 dir)
        {
            Vector3 tipDir = TipLocal.sqrMagnitude > 0f ? TipLocal.normalized : Vector3.forward;
            Vector3 upHint = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? transform.forward : Vector3.up;
            return Quaternion.LookRotation(dir, upHint) * Quaternion.Inverse(Quaternion.LookRotation(tipDir, Vector3.up));
        }
    }
}
