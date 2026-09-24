using System;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// What is on the knife's blade (jam, butter or nothing) and the knife's primary action.
    ///
    /// <see cref="Use"/> (Left Click while held, routed by <c>MealEater</c>) acts on what the player aims at:
    /// a <see cref="SpreadSource"/> coats the blade (replacing any coating); a bread with nothing on it takes
    /// the coating (<see cref="FoodItem.TryApplyTopping"/>) and the blade is clean again. Anything else: nothing.
    /// A future cut on a cuttable target belongs in <see cref="Use"/> too - not built yet.
    ///
    /// World rule: what is on the blade moves to what the knife hits hard enough. A coated knife that strikes
    /// a fixed surface (no Rigidbody - wall, floor, a door leaf) at <see cref="transferSpeed"/> or more along
    /// the contact normal leaves a stain there (attached with <see cref="SurfaceMount"/>, so it rides a door)
    /// and comes away clean. Any throw can do it; placing or a soft bump cannot. Independent of
    /// <see cref="KnifeStick"/>: a strong throw into a door both stains and sticks.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class KnifeCoating : MonoBehaviour
    {
        /// <summary>Raised when the coating leaves on an impact: knife, what it was, the surface, contact point. Nothing listens yet.</summary>
        public static event Action<KnifeCoating, SpreadType, Collider, Vector3> Transferred;

        [Header("Blade visual")]
        [SerializeField] private GameObject jamVisual;
        [SerializeField] private GameObject butterVisual;

        [Header("Impact transfer")]
        [Tooltip("Impact speed along the contact normal (m/s) that wipes the coating onto the surface.")]
        [SerializeField] private float transferSpeed = 3.5f;
        [Tooltip("Inactive stain objects cloned at the impact (local up = surface normal).")]
        [SerializeField] private GameObject jamStainTemplate;
        [SerializeField] private GameObject butterStainTemplate;
        [Tooltip("Lift off the surface, against z-fighting.")]
        [SerializeField] private float surfaceOffset = 0.003f;

        private Interactable _item;
        private SpreadType _spread;
        private Vector3 _lastVelocity;

        public SpreadType CurrentSpread => _spread;

        private Interactable Item => _item != null ? _item : (_item = GetComponent<Interactable>());

        private void Awake() => Refresh();

        public void SetSpread(SpreadType spread)
        {
            _spread = spread;
            Refresh();
        }

        public void ClearSpread() => SetSpread(SpreadType.None);

        /// <summary>The knife's primary action on <paramref name="target"/> (what the player aims at). True if something happened.</summary>
        public bool Use(Collider target)
        {
            if (target == null)
                return false;

            var source = target.GetComponentInParent<SpreadSource>();
            if (source != null && source.Spread != SpreadType.None)
            {
                SetSpread(source.Spread);
                return true;
            }

            var food = target.GetComponentInParent<FoodItem>();
            if (food != null && _spread != SpreadType.None && food.TryApplyTopping(_spread))
            {
                ClearSpread();
                return true;
            }
            return false;
        }

        // Velocity before the solver resolves a contact (OnCollisionEnter already sees the bounce).
        private void FixedUpdate()
        {
            var body = Item.Body;
            if (!body.isKinematic)
                _lastVelocity = body.linearVelocity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_spread == SpreadType.None || Item.IsHeld)
                return;
            if (collision.collider is CharacterController || collision.rigidbody != null)
                return; // the player's body / moving bodies: no stain for now, the coating stays

            ContactPoint contact = collision.GetContact(0);
            Vector3 normal = contact.normal;
            if (Vector3.Dot(normal, _lastVelocity) > 0f)
                normal = -normal; // face back toward where the knife came from
            if (Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal)) < transferSpeed)
                return;

            GameObject template = _spread == SpreadType.Jam ? jamStainTemplate : butterStainTemplate;
            if (template != null)
            {
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Vector3.up);
                GameObject stain = Instantiate(template, contact.point + normal * surfaceOffset, rot);
                stain.name = _spread + "Stain";
                stain.SetActive(true);
                SurfaceMount.Attach(stain.transform, collision.collider);
            }

            SpreadType was = _spread;
            ClearSpread();
            Transferred?.Invoke(this, was, collision.collider, contact.point);
        }

        private void Refresh()
        {
            if (jamVisual != null) jamVisual.SetActive(_spread == SpreadType.Jam);
            if (butterVisual != null) butterVisual.SetActive(_spread == SpreadType.Butter);
        }
    }
}
