using UnityEngine;
using CreatureExperiment.DailyLife;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Story
{
    /// <summary>
    /// Story presentation on a <see cref="StoryNpc"/>: smoking and holding something out. No Animator - the arm pivots are
    /// pointed from the shoulder at a target point (and shortened a little, standing in for a bent elbow) in LateUpdate,
    /// after <see cref="StoryNpc"/>'s walk swing.
    ///
    /// Smoking (<see cref="StartSmoking"/> / <see cref="StopSmoking"/>): a copy of <see cref="cigaretteTemplate"/> (the
    /// store's cigarette, for its look and its smoke thread) sits in the right hand - colliders off, lit, its own burn-down
    /// stopped, so it is a prop only (never an inventory item, never used up). Loop: hand at the hip for
    /// <see cref="restSeconds"/>, up to the mouth, a drag of <see cref="dragSeconds"/>, down again (the prop's own smoke
    /// thread keeps rising throughout).
    ///
    /// Offering (<see cref="Offer"/>): the left hand is held out and the given item rests on it (kinematic) until the player
    /// takes it (<see cref="Interactable.IsHeld"/>) - then the arm drops. <see cref="ClearOffer"/> removes an untaken item.
    /// </summary>
    public class StoryNpcSmoking : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform armR;
        [SerializeField] private Transform armL;
        [Tooltip("Arm length at pivot scale 1 (the arm box hangs this far below its pivot).")]
        [SerializeField] private float armLength = 0.62f;
        [Tooltip("Inactive store cigarette cloned as the prop.")]
        [SerializeField] private GameObject cigaretteTemplate;

        [Header("Targets (visual-root local)")]
        [SerializeField] private Vector3 mouthPoint = new Vector3(0.02f, 1.58f, 0.17f);
        [SerializeField] private Vector3 restPoint = new Vector3(0.26f, 1.0f, 0.2f);
        [SerializeField] private Vector3 offerPoint = new Vector3(-0.2f, 1.2f, 0.5f);

        [Header("Loop (seconds)")]
        [SerializeField] private float restSeconds = 4.5f;
        [SerializeField] private float raiseSeconds = 0.5f;
        [SerializeField] private float dragSeconds = 2f;

        private bool _smoking;
        private GameObject _cig;
        private float _t;
        private float _raise;        // 0 = rest, 1 = at the mouth
        private Interactable _offered;
        private float _offerBlend;
        private bool _touched;

        public bool IsSmoking => _smoking;
        public Interactable OfferedItem => _offered;
        public bool HasUntakenOffer => _offered != null && !_offered.IsHeld;

        public void StartSmoking()
        {
            if (_smoking)
                return;
            _smoking = true;
            _t = 0f;
            _raise = 0f;
            if (cigaretteTemplate != null && _cig == null)
            {
                _cig = Instantiate(cigaretteTemplate, visualRoot != null ? visualRoot : transform, false);
                _cig.name = "Mike_Cigarette (story prop)";
                _cig.transform.localScale = cigaretteTemplate.transform.localScale;
                foreach (var c in _cig.GetComponentsInChildren<Collider>(true))
                    c.enabled = false;
                if (_cig.TryGetComponent(out Rigidbody body))
                {
                    body.isKinematic = true;
                    body.detectCollisions = false;
                }
                _cig.SetActive(true);
                if (_cig.TryGetComponent(out Cigarette cig))
                {
                    cig.Ignite();
                    cig.enabled = false; // keep it lit and full length: no burn-down, no spent butt
                }
            }
        }

        public void StopSmoking()
        {
            _smoking = false;
            if (_cig != null)
                Destroy(_cig);
            _cig = null;
        }

        /// <summary>Hold <paramref name="item"/> out on the left hand until the player takes it.</summary>
        public void Offer(Interactable item)
        {
            _offered = item;
            if (item != null && item.TryGetComponent(out Rigidbody body))
            {
                body.isKinematic = true;
                body.linearVelocity = Vector3.zero;
            }
            PlaceOffered();
        }

        /// <summary>Remove an offered item the player didn't take (it disappears with the NPC's hand).</summary>
        public void ClearOffer()
        {
            if (HasUntakenOffer)
                Destroy(_offered.gameObject);
            _offered = null;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            // Right arm: smoking loop
            if (_smoking)
            {
                _t += dt;
                float cycle = restSeconds + raiseSeconds * 2f + dragSeconds;
                float c = _t % cycle;
                float target;
                if (c < restSeconds) target = 0f;
                else if (c < restSeconds + raiseSeconds) target = (c - restSeconds) / raiseSeconds;
                else if (c < restSeconds + raiseSeconds + dragSeconds) target = 1f;
                else target = 1f - (c - restSeconds - raiseSeconds - dragSeconds) / raiseSeconds;
                _raise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(target));

                Vector3 handTarget = Vector3.Lerp(restPoint, mouthPoint, _raise);
                Point(armR, handTarget);
                if (_cig != null)
                {
                    Vector3 tip = HandTip(armR);
                    Transform root = visualRoot != null ? visualRoot : transform;
                    _cig.transform.position = tip;
                    _cig.transform.rotation = root.rotation * Quaternion.Euler(-10f - 25f * _raise, -30f * (1f - _raise), 0f);
                }
                _touched = true;
            }

            // Left arm: holding something out
            if (_offered != null && _offered.IsHeld)
                _offered = null; // taken
            _offerBlend = Mathf.MoveTowards(_offerBlend, _offered != null ? 1f : 0f, dt * 3f);
            if (_offerBlend > 0f)
            {
                Vector3 shoulder = armL != null ? armL.localPosition : Vector3.zero;
                Vector3 down = shoulder + Vector3.down * armLength;
                Point(armL, Vector3.Lerp(down, offerPoint, _offerBlend));
                PlaceOffered();
                _touched = true;
            }

            if (!_smoking && _offerBlend <= 0f && _touched)
            {
                // Hand the arms back to the walk swing.
                if (armR != null) armR.localScale = Vector3.one;
                if (armL != null) armL.localScale = Vector3.one;
                _touched = false;
            }
        }

        // Point the arm pivot from its shoulder at a visual-local target; shorten it if the target is closer than the arm.
        private void Point(Transform arm, Vector3 target)
        {
            if (arm == null)
                return;
            Vector3 dir = target - arm.localPosition;
            float len = dir.magnitude;
            if (len < 0.001f)
                return;
            arm.localRotation = Quaternion.FromToRotation(Vector3.down, dir / len);
            arm.localScale = new Vector3(1f, Mathf.Clamp(len / armLength, 0.4f, 1f), 1f);
        }

        private Vector3 HandTip(Transform arm) => arm.TransformPoint(new Vector3(0f, -armLength, 0f));

        private void PlaceOffered()
        {
            if (_offered == null || _offered.IsHeld || armL == null)
                return;
            _offered.transform.position = HandTip(armL) + Vector3.up * 0.06f;
            Transform root = visualRoot != null ? visualRoot : transform;
            _offered.transform.rotation = root.rotation;
        }

        private void OnDisable() => StopSmoking();
    }
}
