using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A store's merchandise: which store sells it, its price, and whether it has been paid for. Sits on the store's
    /// product templates, so every copy a shelf hands out starts unpaid. Items without this component are the
    /// player's own things. Plain data - <see cref="CheckoutCounter"/> decides what gets paid and when.
    /// The price here (on the template) is the only copy: the shelf <see cref="PriceTag"/>, the HUD and the
    /// checkout all read it.
    ///
    /// Unpaid packages are closed: nothing may be taken out of or used up from a package (egg carton, bacon pack,
    /// bread bag, jam jar, butter pack) until it is paid. Each content path asks <see cref="RefuseUnpaidUse"/> before
    /// it changes anything - it is keyed on this component alone (no product names), so the player's own food
    /// (no StoreProduct) and paid goods are never affected, and loose contents of a paid package carry no price.
    /// </summary>
    public class StoreProduct : MonoBehaviour
    {
        /// <summary>Shown when an unpaid package's contents are asked for.</summary>
        public const string UnpaidUseNotice = "구매 후 사용할 수 있습니다.";

        private static ObjectiveHUD s_hud;

        [Tooltip("Store that sells this - matches CheckoutCounter.storeId.")]
        [SerializeField] private string storeId = "GroceryStore";
        [Tooltip("US cents ($4.50 = 450).")]
        [SerializeField] private int price = 100;
        [Tooltip("Read-only at runtime - set by the checkout.")]
        [SerializeField] private bool paid;

        public string StoreId => storeId;
        /// <summary>Price in US cents.</summary>
        public int Price => price;
        public bool IsPaid => paid;

        /// <summary>Not yet paid for, and sold by <paramref name="store"/>.</summary>
        public bool IsUnpaidOf(string store) => !paid && storeId == store;

        public void MarkPaid() => paid = true;

        /// <summary>
        /// <paramref name="part"/> (a package, or any child of it such as its content zone) belongs to a store product
        /// that is not paid for yet.
        /// </summary>
        public static bool IsUnpaidPackage(Component part)
        {
            var product = part != null ? part.GetComponentInParent<StoreProduct>(true) : null;
            return product != null && !product.paid;
        }

        /// <summary>
        /// Call before taking anything out of / using up <paramref name="part"/>'s package. Unpaid: shows
        /// <see cref="UnpaidUseNotice"/> and returns true - the caller must then change nothing. Otherwise false.
        /// </summary>
        public static bool RefuseUnpaidUse(Component part)
        {
            if (!IsUnpaidPackage(part))
                return false;
            if (s_hud == null)
                s_hud = FindFirstObjectByType<ObjectiveHUD>();
            if (s_hud != null)
                s_hud.ShowNotice(UnpaidUseNotice);
            return true;
        }
    }
}
