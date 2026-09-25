using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A one-food spot on a pick-up-able object - the frying pan (<see cref="cooks"/> on).
    /// A dedicated <see cref="IOptionalHeldItemReceiver"/>: while the player holds food, aiming at the object
    /// offers "올려놓기"; any other held item is refused ("놓을 수 없습니다") - no swap, never placed on the pan.
    /// With empty hands the pan is a plain pickup. Food thrown onto it (F / Right Click) is caught the same way
    /// (<see cref="IThrownItemReceiver"/>).
    ///
    /// Received food is parented to <see cref="foodPoint"/> (so it travels with the pan), held still and
    /// kept from colliding with its host (<see cref="FoodMount"/>). It stays a normal
    /// <see cref="Interactable"/> and can be picked straight back up; the slot frees itself as soon as
    /// the food is held again, leaves the point, or is gone (eaten).
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class FoodSlot : MonoBehaviour, IOptionalHeldItemReceiver, IThrownItemReceiver
    {
        [Tooltip("Where the food's pivot (its bottom) sits.")]
        [SerializeField] private Transform foodPoint;
        [Tooltip("Food here cooks while this object sits on a stove burner (the frying pan).")]
        [SerializeField] private bool cooks;
        [Tooltip("Catch assist for thrown food: how far past the pan's collider edge (m) a falling throw still lands in it.")]
        [SerializeField] private float catchMargin = 0.06f;

        [Header("Focus label")]
        [SerializeField] private string placePrompt = "올려놓기";
        [SerializeField] private string occupiedPrompt = "이미 음식이 있습니다";
        [Tooltip("Shown for a held item that is not food (the pan owns the aim - nothing is swapped or placed on it).")]
        [SerializeField] private string notFoodPrompt = "놓을 수 없습니다";

        private Interactable _host;
        private FoodItem _food;
        private Collider[] _foodColliders;

        public bool Cooks => cooks;
        public FoodItem Food => _food;
        public bool IsOccupied => _food != null;

        /// <summary>True while this slot cooks and its host sits on a stove burner.</summary>
        public bool IsHeating => cooks && StoveBurner.IsOnBurner(Host);

        private Interactable Host => _host != null ? _host : (_host = GetComponent<Interactable>());

        // --- IFocusTarget (shown only while food is held and aimed here)
        public string FocusName => placePrompt;
        public Transform FocusTransform => transform;
        public void SetFocused(bool focused) => Host.SetFocused(focused);

        // --- IOptionalHeldItemReceiver
        public float MaxReach => 0f;
        public bool IsDedicated => true;
        public float CatchMargin => catchMargin;

        public bool AppliesTo(Interactable item) => item != null && item.GetComponent<FoodItem>() != null;

        public bool CanReceive(Interactable item) => item != null && !IsOccupied && !Host.IsHeld && item.GetComponent<FoodItem>() != null;

        public string GetRejectPrompt(Interactable item)
        {
            if (item == null || item.GetComponent<FoodItem>() == null)
                return notFoodPrompt;
            return IsOccupied ? occupiedPrompt : null;
        }

        public void Receive(Interactable item)
        {
            var food = item != null ? item.GetComponent<FoodItem>() : null;
            if (food == null)
                return;

            _foodColliders = FoodMount.Attach(item, foodPoint != null ? foodPoint : transform);
            FoodMount.IgnoreHost(this, _foodColliders, true);
            _food = food;
            food.SetSlot(this);
        }

        private void Update()
        {
            if (_food == null)
            {
                _foodColliders = null; // eaten / destroyed
                return;
            }

            Transform point = foodPoint != null ? foodPoint : transform;
            if (_food.Interactable.IsHeld || _food.transform.parent != point)
            {
                FoodMount.IgnoreHost(this, _foodColliders, false);
                _food.SetSlot(null);
                _food = null;
                _foodColliders = null;
                return;
            }
            FoodMount.IgnoreHost(this, _foodColliders, true);
        }
    }
}
