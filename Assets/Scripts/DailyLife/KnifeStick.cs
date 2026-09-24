using System;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A knife thrown with a Strong Throw flies tip first and, if it meets a fixed surface (a collider with
    /// no Rigidbody - wall, floor, furniture, a door leaf) hard enough and head-on enough, sticks in it: tip a
    /// little into the surface, held still (kinematic) and attached to that surface (<see cref="SurfaceMount"/>),
    /// so it swings along with a door. It stays a normal <see cref="Interactable"/> - E picks it back out (the
    /// pickup re-parents it to the hand, which ends the attachment) and it can be thrown again. A normal (F) throw is left to plain physics. Anything with a
    /// Rigidbody (items, the creature) is not stuck into here.
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

        private Interactable _item;
        private Vector3 _lastVelocity;
        private bool _armed;
        private Vector3 _looseScale = Vector3.one;

        public bool IsStuck { get; private set; }

        private Interactable Item => _item != null ? _item : (_item = GetComponent<Interactable>());

        private void OnEnable() => PhysicalEvents.Raised += OnPhysicalEvent;
        private void OnDisable() => PhysicalEvents.Raised -= OnPhysicalEvent;

        // Raised by the thrower right after the launch velocity is set: turn the tip into the flight, no tumbling.
        private void OnPhysicalEvent(PhysicalEvent evt)
        {
            if (evt.Interactable != Item || evt.Kind != PhysicalEventKind.Throw)
                return;
            _armed = Item.LastThrowMode == ThrowMode.Strong;
            if (!_armed)
                return;

            var body = Item.Body;
            Vector3 v = body.linearVelocity;
            if (v.sqrMagnitude < 0.01f)
                return;
            Quaternion rot = TipAlong(v.normalized);
            transform.rotation = rot;
            body.rotation = rot;
            body.angularVelocity = Vector3.zero;
            _lastVelocity = v;
        }

        private void FixedUpdate()
        {
            var body = Item.Body;
            if (!body.isKinematic)
                _lastVelocity = body.linearVelocity;
        }

        private void Update()
        {
            if (IsStuck && Item.IsHeld)
            {
                // Taken out: the hand already re-parented it; undo the scale compensation of a scaled mount.
                IsStuck = false;
                transform.localScale = _looseScale;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!_armed || collision.collider is CharacterController)
                return;
            _armed = false; // first real contact only - no sticking after a bounce

            if (Item.IsHeld || Item.LastThrowMode != ThrowMode.Strong || collision.rigidbody != null)
                return;

            float speed = _lastVelocity.magnitude;
            if (speed < minImpactSpeed)
                return;
            Vector3 dir = _lastVelocity / speed;
            ContactPoint contact = collision.GetContact(0);
            Vector3 normal = contact.normal;
            if (Vector3.Dot(normal, dir) > 0f)
                normal = -normal;
            if (Vector3.Dot(dir, -normal) < minHeadOn)
                return;

            var body = Item.Body;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;

            Quaternion rot = TipAlong(dir);
            Vector3 tipOffset = rot * Vector3.Scale(TipLocal, transform.lossyScale);
            transform.SetPositionAndRotation(contact.point + dir * embedDepth - tipOffset, rot);
            _looseScale = transform.localScale;
            SurfaceMount.Attach(transform, collision.collider);
            IsStuck = true;
            Stuck?.Invoke(this, collision.collider);
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
