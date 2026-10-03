using System;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A serving dish a meal is eaten from - the plate (fried egg, bacon, toast) and the bowl (ramen / soup). Each dish has
    /// the spots it has (<see cref="eggPoint"/>, <see cref="baconPoint"/>, <see cref="breadPoint"/>, <see cref="soupPoint"/>;
    /// an unset point = this dish doesn't take that food), at most one food per spot, any mix, only edible (cooked) food.
    /// No recipes.
    ///
    /// Food is put on with the held food's Left Click, or served from a pan / pot (<see cref="CookwareServe"/>), or thrown
    /// onto it (<see cref="IThrownItemReceiver"/>). For Interact the dish is just a pickup (E lifts it, food and all).
    ///
    /// Dish + food = one meal unit: served food rides the dish (parented, held still) and is seat-locked
    /// (<see cref="Interactable.SetPickupLock"/>) - it can't be picked off or eaten on its own; an aim at it is an aim at the
    /// dish. Item-to-item use still reaches the food itself (a coated knife spreads jam on the toast on the plate).
    /// A dish with at least one edible food on it is a meal: <see cref="Eat"/> (Left Click, held or aimed - <c>MealEater</c>)
    /// destroys the whole dish, food included. No washing up.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class MealPlate : MonoBehaviour, IOptionalHeldItemReceiver, IThrownItemReceiver
    {
        /// <summary>Raised when a meal is eaten, just before the plate is destroyed. Nothing listens yet.</summary>
        public static event Action<MealPlate> Eaten;

        [SerializeField] private Transform eggPoint;
        [SerializeField] private Transform baconPoint;
        [SerializeField] private Transform breadPoint;
        [Tooltip("Ramen / soup (a bowl). Empty on a plate.")]
        [SerializeField] private Transform soupPoint;
        [Tooltip("Catch assist for thrown food: how far past the dish's collider edge (m) a falling throw still lands on it.")]
        [SerializeField] private float catchMargin = 0.06f;

        [Header("Labels")]
        [Tooltip("Left Click hint while holding food it takes (\"LMB 올려놓기\").")]
        [SerializeField] private string placePrompt = "올려놓기";
        [SerializeField] private string wrongFoodPrompt = "익혀야 담을 수 있어.";
        [SerializeField] private string occupiedPrompt = "더 담을 수 없어.";
        [Tooltip("Food this dish has no spot for (ramen on a plate, an egg in a bowl).")]
        [SerializeField] private string notFoodPrompt = "여기에는 담을 수 없어.";
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
        private readonly Seat _soup = new Seat();
        private Interactable _host;
        private bool _wasEdible;

        public Interactable Interactable => _host != null ? _host : (_host = GetComponent<Interactable>());
        public FoodItem Egg => _egg.food;
        public FoodItem Bacon => _bacon.food;
        public FoodItem Bread => _bread.food;
        public FoodItem Soup => _soup.food;

        /// <summary>At least one edible food is on the dish.</summary>
        public bool IsEdible => IsEdibleSeat(_egg) || IsEdibleSeat(_bacon) || IsEdibleSeat(_bread) || IsEdibleSeat(_soup);

        // --- IFocusTarget
        public string FocusName => placePrompt;
        public Transform FocusTransform => transform;
        public void SetFocused(bool focused) => Interactable.SetFocused(focused);

        private void Awake()
        {
            _host = GetComponent<Interactable>();
            _egg.point = eggPoint;
            _bacon.point = baconPoint;
            _bread.point = breadPoint;
            _soup.point = soupPoint;
            RefreshName();
        }

        // --- IOptionalHeldItemReceiver
        public float MaxReach => 0f;
        public bool IsDedicated => true;
        public bool ReceivesWithPrimary => true;
        public float CatchMargin => catchMargin;

        public bool AppliesTo(Interactable item) => item != null && item.GetComponent<FoodItem>() != null;

        public bool CanReceive(Interactable item)
        {
            var food = item != null ? item.GetComponent<FoodItem>() : null;
            Seat seat = food != null ? SeatOf(food.Kind) : null;
            return seat != null && food.IsEdible && seat.food == null && !Interactable.IsHeld;
        }

        public string GetRejectPrompt(Interactable item)
        {
            var food = item != null ? item.GetComponent<FoodItem>() : null;
            Seat seat = food != null ? SeatOf(food.Kind) : null;
            if (seat == null)
                return notFoodPrompt;
            if (!food.IsEdible)
                return wrongFoodPrompt;
            return seat.food != null ? occupiedPrompt : null;
        }

        public void Receive(Interactable item)
        {
            var food = item != null ? item.GetComponent<FoodItem>() : null;
            Seat seat = food != null ? SeatOf(food.Kind) : null;
            if (seat == null)
                return;
            seat.colliders = FoodMount.Attach(item, seat.point);
            FoodMount.IgnoreHost(this, seat.colliders, true);
            seat.food = food;
            seat.food.SetPlate(this);
            item.SetPickupLock(this); // part of the meal now
            RefreshName();
        }

        /// <summary>Eat the meal: the dish and everything on it are gone.</summary>
        public void Eat()
        {
            if (!IsEdible)
                return;
            Eaten?.Invoke(this);
            ConsumeEvents.Raise(gameObject, ConsumeKind.Food);
            Destroy(gameObject);
        }

        // The spot for this kind of food on this dish, or null (no such spot here).
        private Seat SeatOf(FoodKind kind)
        {
            Seat seat;
            switch (kind)
            {
                case FoodKind.Egg: seat = _egg; break;
                case FoodKind.Bacon: seat = _bacon; break;
                case FoodKind.Bread: seat = _bread; break;
                case FoodKind.Ramen:
                case FoodKind.Soup: seat = _soup; break;
                default: seat = null; break;
            }
            return seat != null && seat.point != null ? seat : null;
        }

        private static bool IsEdibleSeat(Seat seat) => seat.food != null && seat.food.IsEdible;

        private void Update()
        {
            Check(_egg);
            Check(_bacon);
            Check(_bread);
            Check(_soup);
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
                seat.food.Interactable.ClearPickupLock(this);
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
