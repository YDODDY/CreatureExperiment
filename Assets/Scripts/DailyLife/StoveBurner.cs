using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// One stove burner a frying pan or pot can be set on - "올려놓기" through the existing
    /// <see cref="IHeldItemReceiver"/> path (aim at the burner while holding + Interact). Anything else
    /// shows the reject prompt. One item per burner.
    ///
    /// On receive, the item is seated on <see cref="placePoint"/>, its colliders come back on (HandOver
    /// leaves them off) and it is held still (kinematic) so it stays put; it can be picked up again as a
    /// normal <see cref="Interactable"/>. The burner watches its occupant and frees itself as soon as the
    /// occupant is held again (by anyone), leaves the spot, or is gone - no change to the pickup code.
    /// The flame is only a visual for "something is on this burner": no heat, no cooking, no timer.
    /// </summary>
    public class StoveBurner : MonoBehaviour, IHeldItemReceiver
    {
        [Tooltip("Where an item's bottom sits - its forward is the item's facing.")]
        [SerializeField] private Transform placePoint;
        [Tooltip("Shown while an item sits on this burner.")]
        [SerializeField] private GameObject flameVisual;

        [Header("Focus label")]
        [SerializeField] private string placePrompt = "올려놓기";
        [SerializeField] private string rejectPrompt = "올려놓을 수 없습니다";
        [SerializeField] private string occupiedPrompt = "이미 올려져 있습니다";

        [Tooltip("How far the occupant may drift from the place point before the burner counts it as removed.")]
        [SerializeField] private float leaveDistance = 0.1f;

        private static readonly System.Collections.Generic.List<StoveBurner> s_burners = new System.Collections.Generic.List<StoveBurner>();

        private Interactable _occupant;

        public Interactable Occupant => _occupant;
        public bool IsOccupied => _occupant != null;

        /// <summary>True if <paramref name="item"/> sits on any enabled burner (its flame is on).</summary>
        public static bool IsOnBurner(Interactable item)
        {
            if (item == null)
                return false;
            foreach (var burner in s_burners)
                if (burner != null && burner._occupant == item)
                    return true;
            return false;
        }

        private void OnEnable() => s_burners.Add(this);
        private void OnDisable() => s_burners.Remove(this);

        public string FocusName => placePrompt;
        public Transform FocusTransform => transform;

        public void SetFocused(bool focused) { }

        private void Awake() => SetFlame(false);

        public bool CanReceive(Interactable item)
        {
            if (item == null || IsOccupied)
                return false;
            var kitchen = item.GetComponent<KitchenItem>();
            return kitchen != null && (kitchen.Kind == KitchenItemKind.FryingPan || kitchen.Kind == KitchenItemKind.Pot);
        }

        public string GetRejectPrompt(Interactable item) => IsOccupied ? occupiedPrompt : rejectPrompt;

        public void Receive(Interactable item)
        {
            if (item == null)
                return;

            Transform anchor = placePoint != null ? placePoint : transform;
            var kitchen = item.GetComponent<KitchenItem>();
            float rest = kitchen != null ? kitchen.RestHeight : 0f;

            item.transform.SetPositionAndRotation(anchor.position + Vector3.up * rest, anchor.rotation);
            foreach (var col in item.GetComponentsInChildren<Collider>())
                col.enabled = true;

            var body = item.Body;
            body.isKinematic = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            _occupant = item;
            SetFlame(true);
        }

        private void Update()
        {
            if (_occupant == null)
            {
                if (flameVisual != null && flameVisual.activeSelf)
                    SetFlame(false); // occupant destroyed
                return;
            }

            Transform anchor = placePoint != null ? placePoint : transform;
            Vector3 offset = _occupant.transform.position - anchor.position;
            offset.y = 0f;
            if (_occupant.IsHeld || !_occupant.gameObject.activeInHierarchy || offset.magnitude > leaveDistance)
            {
                _occupant = null;
                SetFlame(false);
            }
        }

        private void SetFlame(bool on)
        {
            if (flameVisual != null)
                flameVisual.SetActive(on);
        }
    }
}
