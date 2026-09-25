using System;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A plate a meal is served on: three independent spots - fried egg, fried bacon, bread (raw or
    /// toasted) - at most one of each, any mix. No recipes. A dedicated <see cref="IOptionalHeldItemReceiver"/>:
    /// while the player holds food, aiming at the plate offers "올려놓기" when that food's spot is free and
    /// a reject prompt otherwise; any other held item is refused (no swap, never placed on the plate). With empty
    /// hands it is a plain pickup-able plate. Food thrown onto it is caught the same way (<see cref="IThrownItemReceiver"/>).
    ///
    /// Served food rides the plate (parented, held still) and can still be picked back off one by one.
    /// A plate with at least one edible food on it is a meal: <see cref="Eat"/> destroys the whole plate,
    /// food included (the primary action does it - see <c>MealEater</c>).
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class MealPlate : MonoBehaviour, IOptionalHeldItemReceiver, IThrownItemReceiver
    {
        /// <summary>Raised when a meal is eaten, just before the plate is destroyed. Nothing listens yet.</summary>
        public static event Action<MealPlate> Eaten;

        [SerializeField] private Transform eggPoint;
        [SerializeField] private Transform baconPoint;
        [SerializeField] private Transform breadPoint;
        [Tooltip("Catch assist for thrown food: how far past the plate's collider edge (m) a falling throw still lands on it.")]
        [SerializeField] private float catchMargin = 0.06f;

        [Header("Labels")]
        [SerializeField] private string placePrompt = "올려놓기";
        [SerializeField] private string wrongFoodPrompt = "익히지 않은 계란·베이컨은 올릴 수 없습니다";
        [SerializeField] private string occupiedPrompt = "이미 올려져 있습니다";
        [SerializeField] private string notFoodPrompt = "놓을 수 없습니다";
        [SerializeField] private string emptyName = "접시";
        [SerializeField] private string mealName = "식사 (좌클릭: 먹기)";

        private class Seat
        {
            public Transform point;
            public FoodItem food;
            public Collider[] colliders;
        }

        private readonly Seat _egg = new Seat();
        private readonly Seat _bacon = new Seat();
        private readonly Seat _bread = new Seat();
        private Interactable _host;
        private bool _wasEdible;

        public Interactable Interactable => _host != null ? _host : (_host = GetComponent<Interactable>());
        public FoodItem Egg => _egg.food;
        public FoodItem Bacon => _bacon.food;
        public FoodItem Bread => _bread.food;

        /// <summary>At least one edible food is on the plate.</summary>
        public bool IsEdible => IsEdibleSeat(_egg) || IsEdibleSeat(_bacon) || IsEdibleSeat(_bread);

        // --- IFocusTarget (shown only while food is held and aimed here)
        public string FocusName => placePrompt;
        public Transform FocusTransform => transform;
        public void SetFocused(bool focused) => Interactable.SetFocused(focused);

        private void Awake()
        {
            _host = GetComponent<Interactable>();
            _egg.point = eggPoint;
            _bacon.point = baconPoint;
            _bread.point = breadPoint;
            RefreshName();
        }

        // --- IOptionalHeldItemReceiver
        public float MaxReach => 0f;
        public bool IsDedicated => true;
        public float CatchMargin => catchMargin;

        public bool AppliesTo(Interactable item) => item != null && item.GetComponent<FoodItem>() != null;

        public bool CanReceive(Interactable item)
        {
            var seat = SeatFor(item);
            return seat != null && seat.food == null && !Interactable.IsHeld;
        }

        public string GetRejectPrompt(Interactable item)
        {
            var food = item != null ? item.GetComponent<FoodItem>() : null;
            if (food == null)
                return notFoodPrompt;
            return SeatFor(item) == null ? wrongFoodPrompt : occupiedPrompt;
        }

        public void Receive(Interactable item)
        {
            var seat = SeatFor(item);
            if (seat == null || seat.point == null)
                return;
            seat.colliders = FoodMount.Attach(item, seat.point);
            FoodMount.IgnoreHost(this, seat.colliders, true);
            seat.food = item.GetComponent<FoodItem>();
            seat.food.SetPlate(this);
            RefreshName();
        }

        /// <summary>Eat the meal: the plate and everything on it are gone.</summary>
        public void Eat()
        {
            if (!IsEdible)
                return;
            Eaten?.Invoke(this);
            Destroy(gameObject);
        }

        private Seat SeatFor(Interactable item)
        {
            var food = item != null ? item.GetComponent<FoodItem>() : null;
            if (food == null || !food.IsEdible)
                return null;
            switch (food.Kind)
            {
                case FoodKind.Egg: return _egg;
                case FoodKind.Bacon: return _bacon;
                case FoodKind.Bread: return _bread;
                default: return null;
            }
        }

        private static bool IsEdibleSeat(Seat seat) => seat.food != null && seat.food.IsEdible;

        private void Update()
        {
            Check(_egg);
            Check(_bacon);
            Check(_bread);
            if (IsEdible != _wasEdible)
                RefreshName();
        }

        private void Check(Seat seat)
        {
            if (seat.food == null)
            {
                seat.colliders = null; // eaten / destroyed
                return;
            }
            if (seat.food.Interactable.IsHeld || seat.food.transform.parent != seat.point)
            {
                FoodMount.IgnoreHost(this, seat.colliders, false);
                seat.food.SetPlate(null);
                seat.food = null;
                seat.colliders = null;
                return;
            }
            FoodMount.IgnoreHost(this, seat.colliders, true);
        }

        private void RefreshName()
        {
            _wasEdible = IsEdible;
            Interactable.SetDisplayName(_wasEdible ? mealName : emptyName);
        }
    }
}
