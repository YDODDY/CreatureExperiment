using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// One line of the café menu, kept on the (inactive) item template it serves: the name the barista and the menu
    /// board show and its price in US cents. <see cref="BaristaCounter"/> reads every one under its menu root, in
    /// hierarchy order - the one price source for the order, the charge and the board. Data only; a served copy keeps it
    /// harmlessly (it is already paid for - café items carry no <see cref="StoreProduct"/>).
    /// Also used by the fast-food counter and the pub bar (same order → pay → pickup flow). A <see cref="Category"/> groups
    /// a longer menu ("음료" / "안주"): with two or more categories the counter asks the category first.
    /// </summary>
    public class CafeMenuItem : MonoBehaviour
    {
        [SerializeField] private string menuName = "Americano";
        [Tooltip("US cents ($3.50 = 350).")]
        [SerializeField] private int price = 350;
        [Tooltip("Optional menu group (음료 / 안주). Empty = no grouping (the café).")]
        [SerializeField] private string category = "";

        public string MenuName => menuName;
        public int Price => price;
        public string Category => category ?? "";
    }
}
