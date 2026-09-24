using System;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    public enum FoodKind
    {
        Egg,
        Bacon,
        Bread
    }

    public enum FoodState
    {
        Raw,
        Cooked
    }

    /// <summary>
    /// A piece of food, sitting next to its <see cref="Interactable"/> (picked up / placed / thrown the
    /// usual way). It cooks while it lies in a heating <see cref="FoodSlot"/> (a frying pan on a stove
    /// burner): a plain timer, Raw -> Cooked once, never burns. The raw / cooked look is a child swap.
    ///
    /// Cooking finishes with a short one-shot "puff" (<see cref="cookPuff"/>): a few small blobs rise,
    /// swell and vanish - only at the moment it turns cooked, never while cooking.
    ///
    /// Eating is the primary action (Left Click, <c>MealEater</c>) on edible food - held or aimed at up
    /// close. Interact stays the plain physical pickup / place / throw even when the food is edible.
    /// Food served on a <see cref="MealPlate"/> is eaten with the plate.
    ///
    /// Bread can also carry one topping (jam or butter), spread on with a coated knife
    /// (<see cref="TryApplyTopping"/>). It is independent of cooking - toasting keeps it - and does not
    /// change what the bread is: still Bread, still edible, still fits the plate's bread spot.
    /// </summary>
    public class FoodItem : MonoBehaviour
    {
        /// <summary>Raised when any food is eaten, just before it is destroyed. Nothing listens yet.</summary>
        public static event Action<FoodItem> Eaten;

        [SerializeField] private FoodKind kind;
        [SerializeField] private FoodState state = FoodState.Raw;
        [Tooltip("Can be eaten without cooking (bread).")]
        [SerializeField] private bool edibleRaw;
        [SerializeField] private float cookTime = 4f;
        [SerializeField] private string cookedName;

        [Header("Visuals")]
        [SerializeField] private GameObject rawVisual;
        [SerializeField] private GameObject cookedVisual;
        [Tooltip("Optional look for raw food before it first goes into a pan (an egg in its shell).")]
        [SerializeField] private GameObject uncrackedVisual;

        [Header("Topping (bread only)")]
        [SerializeField] private SpreadType topping;
        [Tooltip("Thin layers on the top face, shown for the current topping (raw and toasted alike).")]
        [SerializeField] private GameObject jamTopping;
        [SerializeField] private GameObject butterTopping;

        [Header("Cook-complete puff")]
        [Tooltip("Inactive child shown briefly when the food turns cooked.")]
        [SerializeField] private Transform cookPuff;
        [SerializeField] private float puffDuration = 0.8f;
        [SerializeField] private float puffRise = 0.12f;

        private Interactable _interactable;
        private float _cookProgress;
        private bool _cracked;
        private float _puffTime = -1f;
        private Vector3 _puffPos, _puffScale;

        public FoodKind Kind => kind;
        public FoodState State => state;
        public bool IsCooked => state == FoodState.Cooked;
        public bool IsEdible => IsCooked || edibleRaw;
        public SpreadType Topping => topping;
        public float CookProgress01 => cookTime > 0f ? Mathf.Clamp01(_cookProgress / cookTime) : 1f;

        /// <summary>The pan slot holding this food, or null. Set by <see cref="FoodSlot"/>.</summary>
        public FoodSlot Slot { get; private set; }

        /// <summary>The meal plate this food is served on, or null. Set by <see cref="MealPlate"/>.</summary>
        public MealPlate Plate { get; private set; }

        public Interactable Interactable => _interactable != null ? _interactable : (_interactable = GetComponent<Interactable>());

        private void Awake()
        {
            _interactable = GetComponent<Interactable>();
            RefreshVisuals();
            if (cookPuff != null)
            {
                _puffPos = cookPuff.localPosition;
                _puffScale = cookPuff.localScale;
                cookPuff.gameObject.SetActive(false);
            }
        }

        internal void SetPlate(MealPlate plate) => Plate = plate;

        internal void SetSlot(FoodSlot slot)
        {
            Slot = slot;
            if (slot != null && slot.Cooks)
                _cracked = true;
            RefreshVisuals();
        }

        private void Update()
        {
            if (state == FoodState.Raw && Slot != null && Slot.IsHeating)
            {
                _cookProgress += Time.deltaTime;
                if (_cookProgress >= cookTime)
                    Cook();
            }

            UpdatePuff();
        }

        private void Cook()
        {
            state = FoodState.Cooked;
            _cracked = true;
            if (!string.IsNullOrEmpty(cookedName))
                Interactable.SetDisplayName(cookedName);
            RefreshVisuals();
            if (cookPuff != null)
            {
                _puffTime = 0f;
                cookPuff.gameObject.SetActive(true);
            }
        }

        // One-shot: rise, swell, then shrink away. Rotation-free so it reads the same from any side.
        private void UpdatePuff()
        {
            if (cookPuff == null || _puffTime < 0f)
                return;

            _puffTime += Time.deltaTime;
            float t = puffDuration > 0f ? Mathf.Clamp01(_puffTime / puffDuration) : 1f;
            cookPuff.localPosition = _puffPos + Vector3.up * (puffRise * t);
            cookPuff.localScale = _puffScale * ((0.6f + 0.8f * t) * (1f - t * t * t));
            if (t < 1f)
                return;

            cookPuff.localPosition = _puffPos;
            cookPuff.localScale = _puffScale;
            cookPuff.gameObject.SetActive(false);
            _puffTime = -1f;
        }

        /// <summary>Spread <paramref name="spread"/> on a bread that has nothing on it yet. False (unchanged) otherwise - no layering, no mixing.</summary>
        public bool TryApplyTopping(SpreadType spread)
        {
            if (kind != FoodKind.Bread || spread == SpreadType.None || topping != SpreadType.None)
                return false;
            topping = spread;
            RefreshVisuals();
            return true;
        }

        /// <summary>Eat it: the object is gone.</summary>
        public void Eat()
        {
            if (!IsEdible)
                return;
            Eaten?.Invoke(this);
            Destroy(gameObject);
        }

        private void RefreshVisuals()
        {
            bool cooked = state == FoodState.Cooked;
            bool showUncracked = !cooked && !_cracked && uncrackedVisual != null;
            if (cookedVisual != null) cookedVisual.SetActive(cooked);
            if (uncrackedVisual != null) uncrackedVisual.SetActive(showUncracked);
            if (rawVisual != null) rawVisual.SetActive(!cooked && !showUncracked);
            if (jamTopping != null) jamTopping.SetActive(topping == SpreadType.Jam);
            if (butterTopping != null) butterTopping.SetActive(topping == SpreadType.Butter);
        }
    }
}
