using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Marks one item the player sorts and tags which <see cref="SortKind"/> it is. It carries no
    /// sorting logic - <see cref="DailyLifeDirector"/> reads <see cref="Kind"/> and the live
    /// <c>transform.position</c> each frame and decides. It also remembers the pose it started the
    /// day at so the day-rollover can put it back.
    ///
    /// Sits on the same GameObject as the existing <c>Interactable</c> (pickup / place / throw are
    /// reused unchanged); this only adds the kind tag + a spawn-pose reset.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SortableItem : MonoBehaviour
    {
        [Tooltip("Which sorting area this item belongs in.")]
        [SerializeField] private SortKind kind = SortKind.Cube;

        /// <summary>The area this item counts as sorted in when it is physically inside that area.</summary>
        public SortKind Kind => kind;

        private Interactable _interactable;
        private Rigidbody _body;
        private Vector3 _spawnPos;
        private Quaternion _spawnRot;

        /// <summary>True while the item is in a hand (player or creature). A held item never counts as sorted.</summary>
        public bool IsHeld => _interactable != null && _interactable.IsHeld;

        private void Awake()
        {
            _interactable = GetComponent<Interactable>();
            _body = GetComponent<Rigidbody>();
            _spawnPos = transform.position;
            _spawnRot = transform.rotation;
        }

        /// <summary>
        /// Put the item back exactly where the day started it, at rest. Mirrors the teardown in
        /// <c>PlayerInteractor.Place</c>. No-op while something is still holding it - that carrier's
        /// own release will restore it.
        /// </summary>
        public void ResetToSpawn()
        {
            if (IsHeld)
                return;

            transform.SetParent(null, worldPositionStays: true);
            transform.SetPositionAndRotation(_spawnPos, _spawnRot);

            if (_body != null)
            {
                _body.isKinematic = false;
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.Sleep();
            }
        }
    }
}
