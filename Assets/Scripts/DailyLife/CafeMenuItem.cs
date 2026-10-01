using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// One line of the café menu, kept on the (inactive) item template it serves: the name the barista and the menu
    /// board show and its price in US cents. <see cref="BaristaCounter"/> reads every one under its menu root, in
    /// hierarchy order - the one price source for the order, the charge and the board. Data only; a served copy keeps it
    /// harmlessly (it is already paid for - café items carry no <see cref="StoreProduct"/>).
    /// </summary>
    public class CafeMenuItem : MonoBehaviour
    {
        [SerializeField] private string menuName = "Americano";
        [Tooltip("US cents ($3.50 = 350).")]
        [SerializeField] private int price = 350;

        public string MenuName => menuName;
        public int Price => price;
    }
}
