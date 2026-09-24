using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>What kind of kitchen object an <c>Interactable</c> is.</summary>
    public enum KitchenItemKind
    {
        FryingPan,
        Pot,
        CuttingBoard,
        Knife,
        Plate,
        Bowl
    }

    /// <summary>
    /// Kitchen identity for a pick-up-able cookware object, sitting next to its <c>Interactable</c>.
    /// Receivers (e.g. a <see cref="StoveBurner"/>) decide what they accept from <see cref="Kind"/> -
    /// no name matching. <see cref="RestHeight"/> is how far the pivot sits above the object's bottom,
    /// so a receiver can seat it flush on a surface.
    /// </summary>
    public class KitchenItem : MonoBehaviour
    {
        [SerializeField] private KitchenItemKind kind;
        [Tooltip("Distance from this object's pivot down to its bottom, in metres.")]
        [SerializeField] private float restHeight;

        public KitchenItemKind Kind => kind;
        public float RestHeight => restHeight;
    }
}
