using System;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// How much is left in a food container - eggs in a carton, bacon strips in a pack, slices in a bread bag,
    /// uses left in a jam jar / butter pack, eggs in the fridge door holder - and the look that goes with it.
    /// A plain count, not an inventory: <see cref="TryTake"/> lowers it, <see cref="TryAdd"/> raises it (only
    /// when <see cref="refillable"/>, capped at <see cref="max"/>). At 0 the container stays in the world as it
    /// is - nothing here ever destroys it.
    ///
    /// The count is shown two ways (either or both):
    /// - <see cref="unitRoot"/>: one child per unit (an egg, a strip, a slice); the first <see cref="Current"/>
    ///   children are on, the rest off - so taking one hides the last visible one.
    /// - <see cref="levelPivot"/>: a pivot placed at the content's fixed end (jar bottom, butter block's end);
    ///   its scale along <see cref="levelAxis"/> is Current / Max, so the content shrinks in steps. Off at 0.
    ///
    /// <see cref="current"/> is serialized, so a copy of a half-used container (a store shelf later) keeps
    /// its amount. A story sequence may later change it through <see cref="SetCurrent"/>.
    /// </summary>
    public class ConsumableStock : MonoBehaviour
    {
        /// <summary>Raised whenever the amount changes: stock, new amount. Nothing outside listens yet.</summary>
        public static event Action<ConsumableStock, int> AnyChanged;

        /// <summary>Raised on this stock whenever the amount changes.</summary>
        public event Action<ConsumableStock> Changed;

        [SerializeField] private int max = 6;
        [SerializeField] private int current = 6;
        [Tooltip("Can be topped up again (fridge egg holder). Packages you buy (cartons, packs, jars) are not.")]
        [SerializeField] private bool refillable;
        [Tooltip("What the units are (\"Egg\"). A StockRefiller only fills a stock of its own content.")]
        [SerializeField] private string contentId;

        [Header("Look")]
        [Tooltip("Parent whose children are one unit each, in the order they are used up from the end.")]
        [SerializeField] private Transform unitRoot;
        [Tooltip("Pivot at the content's fixed end; scaled along levelAxis by Current / Max.")]
        [SerializeField] private Transform levelPivot;
        [Tooltip("0 = X, 1 = Y, 2 = Z (pivot local axis).")]
        [SerializeField] private int levelAxis = 1;

        [Header("Empty")]
        [Tooltip("Focus name of the container's Interactable once empty (e.g. \"빈 계란판\"). Empty = unchanged.")]
        [SerializeField] private string emptyName;

        private Vector3 _levelFullScale;
        private bool _levelCached;

        public int Max => max;
        public int Current => current;
        public bool IsEmpty => current <= 0;
        public bool IsFull => current >= max;
        public bool Refillable => refillable;
        public string ContentId => contentId;

        private SwingDoor _door;
        private bool _doorLooked;

        /// <summary>
        /// True for storage mounted in a door (the fridge door egg holder) while that door is not fully open or is
        /// swinging: nothing goes in or out through a shut door, even while the door leaf's collider is off.
        /// </summary>
        public bool IsBehindClosedDoor
        {
            get
            {
                if (!_doorLooked) { _door = GetComponentInParent<SwingDoor>(); _doorLooked = true; }
                return _door != null && (!_door.IsOpen || _door.IsMoving);
            }
        }

        private void Awake()
        {
            current = Mathf.Clamp(current, 0, max);
            Refresh();
        }

        /// <summary>Use up one. False (unchanged) when empty.</summary>
        public bool TryTake()
        {
            if (current <= 0)
                return false;
            SetCurrent(current - 1);
            return true;
        }

        /// <summary>
        /// Put up to <paramref name="amount"/> back in (refillable stocks only), never past <see cref="Max"/>.
        /// Returns how many actually went in (0 if not refillable or already full).
        /// </summary>
        public int TryAdd(int amount)
        {
            if (!refillable || amount <= 0)
                return 0;
            int added = Mathf.Min(amount, max - current);
            if (added > 0)
                SetCurrent(current + added);
            return added;
        }

        /// <summary>Set the amount directly (clamped). For a future story sequence / debug - not a gameplay path.</summary>
        public void SetCurrent(int value)
        {
            value = Mathf.Clamp(value, 0, max);
            if (value == current)
                return;
            if (value > current) // increases are rare and explicit (refill) - trace them
                Debug.Log($"[ConsumableStock] {name}: {current} -> {value}/{max}", this);
            current = value;
            Refresh();
            Changed?.Invoke(this);
            AnyChanged?.Invoke(this, current);
        }

        private void Refresh()
        {
            if (unitRoot != null)
                for (int i = 0; i < unitRoot.childCount; i++)
                    unitRoot.GetChild(i).gameObject.SetActive(i < current);

            if (levelPivot != null)
            {
                if (!_levelCached)
                {
                    _levelFullScale = levelPivot.localScale;
                    _levelCached = true;
                }
                float fill = max > 0 ? (float)current / max : 0f;
                Vector3 s = _levelFullScale;
                s[Mathf.Clamp(levelAxis, 0, 2)] *= Mathf.Max(fill, 0.0001f);
                levelPivot.localScale = s;
                levelPivot.gameObject.SetActive(current > 0);
            }

            if (IsEmpty && !string.IsNullOrEmpty(emptyName) && TryGetComponent(out Interactable item))
                item.SetDisplayName(emptyName);
        }
    }
}
