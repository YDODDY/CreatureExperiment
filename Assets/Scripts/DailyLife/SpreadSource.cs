using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>Something a knife can carry and a bread can have on it. No amounts, no mixing - one at a time.</summary>
    public enum SpreadType
    {
        None,
        Jam,
        Butter
    }

    /// <summary>
    /// A jar / dish to spread from (jam, butter). Aiming a held knife at it with the primary action coats the
    /// knife (<see cref="KnifeCoating"/>), using up one of its uses (<see cref="ConsumableStock"/> on the same
    /// object, if any - without one it never runs out). Empty: no more coating; the empty jar / pack stays an
    /// ordinary pick-up-able item. No lid to open.
    /// </summary>
    public class SpreadSource : MonoBehaviour
    {
        [SerializeField] private SpreadType spread = SpreadType.Jam;

        private ConsumableStock _stock;
        private bool _stockLooked;

        public SpreadType Spread => spread;

        private ConsumableStock Stock
        {
            get
            {
                if (!_stockLooked) { _stock = GetComponent<ConsumableStock>(); _stockLooked = true; }
                return _stock;
            }
        }

        /// <summary>True if a knife can still be coated from this.</summary>
        public bool HasSpread => spread != SpreadType.None && (Stock == null || !Stock.IsEmpty);

        /// <summary>Use one portion for a knife. False (nothing used) when empty.</summary>
        public bool TryUse()
        {
            if (!HasSpread)
                return false;
            return Stock == null || Stock.TryTake();
        }
    }
}
