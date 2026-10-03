using System.Collections.Generic;
using UnityEngine;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Serving out of a cookware item - the frying pan onto a plate, the pot into a bowl. Its Left Click while it is in the
    /// hand (<see cref="IHeldPrimaryAction"/>, routed by <c>MealEater</c>):
    /// - food in it + aiming at a serving dish (<see cref="MealPlate"/> - a plate, a bowl): <b>serve</b> - it tips toward the
    ///   dish (<see cref="HeldUseStyle.Serve"/>) and every food the dish takes right now moves onto it, through the dish's own
    ///   acceptance (<see cref="MealPlate.CanReceive"/>) and <see cref="MealPlate.Receive"/>. Whatever the dish refuses stays.
    /// - food in it, aimed anywhere else: nothing moves; <see cref="aimDishHint"/>.
    /// - empty: a short swing (<see cref="HeldUseStyle.Swing"/>) if <see cref="swingWhenEmpty"/> - expression only: no hit test,
    ///   no damage, no reaction. Otherwise nothing.
    /// Food seated in it is seat-locked by its <see cref="FoodSlot"/>, so serving is the way food leaves it (or it is thrown
    /// out with the cookware). The press is always used, so the router never falls through to eating.
    /// One component for every cookware - what differs is data (the hint, swinging or not).
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class CookwareServe : MonoBehaviour, IHeldPrimaryAction
    {
        [Tooltip("How far the dish can be (m) from the camera.")]
        [SerializeField] private float serveReach = 1.8f;
        [Tooltip("Food in it, aimed elsewhere (\"접시에 담을 수 있어.\").")]
        [SerializeField] private string aimDishHint = "접시에 담을 수 있어.";
        [SerializeField] private bool swingWhenEmpty = true;

        private Interactable _item;
        private HeldItemUseMotion _motion;
        private MealPlate _dish;
        private bool _busy;

        public bool PrimaryActive => _busy;

        private Interactable Item => _item != null ? _item : (_item = GetComponent<Interactable>());

        /// <summary>The foods seated in it right now.</summary>
        public List<FoodItem> Foods()
        {
            var list = new List<FoodItem>();
            foreach (var slot in GetComponentsInChildren<FoodSlot>())
                if (slot.Food != null)
                    list.Add(slot.Food);
            return list;
        }

        public bool PrimaryPress(Ray aim)
        {
            if (_busy)
                return true;
            _motion = HeldItemUseMotion.For(Item);

            List<FoodItem> foods = Foods();
            if (foods.Count == 0)
            {
                if (swingWhenEmpty && _motion != null && _motion.PlayOneShot(Item, HeldUseStyle.Swing, null, () => _busy = false))
                    _busy = true;
                return true;
            }

            _dish = AimedDish(aim);
            if (_dish == null)
            {
                ObjectiveHUD.Notice(aimDishHint);
                return true;
            }
            bool any = false;
            foreach (var food in foods)
                any |= _dish.CanReceive(food.Interactable);
            if (!any)
            {
                ObjectiveHUD.Notice(_dish.GetRejectPrompt(foods[0].Interactable));
                return true;
            }

            if (_motion != null && _motion.PlayOneShot(Item, HeldUseStyle.Serve, Serve, () => _busy = false))
            {
                _busy = true;
                return true;
            }
            Serve();
            return true;
        }

        public void PrimaryCancel()
        {
            if (!_busy)
                return;
            _busy = false;
            if (_motion != null)
                _motion.Cancel(Item);
        }

        // Every food the dish still takes moves over; the rest stays.
        private void Serve()
        {
            if (_dish == null)
                return;
            foreach (var slot in GetComponentsInChildren<FoodSlot>())
            {
                FoodItem food = slot.Food;
                if (food == null || !_dish.CanReceive(food.Interactable))
                    continue;
                slot.TakeFood();
                _dish.Receive(food.Interactable);
            }
        }

        private MealPlate AimedDish(Ray aim)
        {
            if (!Physics.Raycast(aim, out RaycastHit hit, serveReach, ~0, QueryTriggerInteraction.Ignore))
                return null;
            var dish = hit.collider.GetComponentInParent<MealPlate>();
            return dish != null && !dish.Interactable.IsHeld ? dish : null;
        }
    }
}
