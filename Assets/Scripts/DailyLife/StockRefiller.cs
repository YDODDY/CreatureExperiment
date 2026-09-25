using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Put what is in the hand back into storage: Left Click (the held item's primary action) while aiming at a
    /// refillable <see cref="ConsumableStock"/> of the same <see cref="contentId"/> - the fridge door egg holder.
    /// E on the holder stays "take one out"; putting in is the held item's own action.
    ///
    /// Two shapes, one component:
    /// - On a loose item (an Egg): it goes in as one unit and the object is gone. Only a whole raw egg counts - a
    ///   fried or cracked one is not storage material, and its Left Click stays "eat".
    /// - On a package with its own stock (an EggsBundle): as many units as fit move over in one click
    ///   (min(free slots, units left)); the package keeps the rest and is never refilled itself.
    /// A full target takes nothing and nothing is used up. Aimed at anything else, the press is reported unused
    /// (so <c>MealEater</c> falls back to eating).
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class StockRefiller : MonoBehaviour, IHeldPrimaryAction
    {
        [SerializeField] private string contentId = "Egg";
        [Tooltip("Reach of the Left Click.")]
        [SerializeField] private float reach = 1.5f;

        private ConsumableStock _own;
        private FoodItem _food;
        private bool _looked;

        public string ContentId => contentId;

        private void Look()
        {
            if (_looked) return;
            _own = GetComponent<ConsumableStock>();
            _food = GetComponent<FoodItem>();
            _looked = true;
        }

        /// <summary>How many units this can hand over right now.</summary>
        public int Available
        {
            get
            {
                Look();
                if (_own != null) return _own.Current;
                return _food == null || _food.IsWholeRawEgg ? 1 : 0;
            }
        }

        /// <summary>True if <paramref name="target"/> is storage this can go into (full or not).</summary>
        public bool AppliesTo(ConsumableStock target)
        {
            Look();
            return target != null && target != _own && target.Refillable && target.ContentId == contentId && !target.IsBehindClosedDoor;
        }

        public bool PrimaryPress(Ray aim)
        {
            Look();
            if (_own == null && Available == 0)
                return false; // a fried egg: not for storage - let the router eat it
            if (!Physics.Raycast(aim, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore))
                return false;
            var target = hit.collider.GetComponentInParent<ConsumableStock>();
            if (!AppliesTo(target))
                return false;

            int moved = target.TryAdd(Available);
            if (moved <= 0)
                return true; // full (or nothing left): the click was aimed right, nothing changes
            if (_own != null)
                _own.SetCurrent(_own.Current - moved);
            else
                Destroy(gameObject); // the loose egg is now one of the holder's
            return true;
        }
    }
}
