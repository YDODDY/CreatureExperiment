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
    /// knife (<see cref="KnifeCoating"/>). Never runs out; no lid to open. The object itself stays an ordinary
    /// pick-up-able item.
    /// </summary>
    public class SpreadSource : MonoBehaviour
    {
        [SerializeField] private SpreadType spread = SpreadType.Jam;

        public SpreadType Spread => spread;
    }
}
