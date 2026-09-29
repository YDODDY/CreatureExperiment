using UnityEngine;

namespace CreatureExperiment.Player
{
    /// <summary>
    /// The player's money - one balance in US cents ($50.00 = 5000), so sums never round. <see cref="TrySpend"/>
    /// takes an amount off only if it is all there; nothing else earns or spends yet. <see cref="FormatUsd"/> is the
    /// one place money is turned into text ("$4.50").
    /// </summary>
    public class PlayerWallet : MonoBehaviour
    {
        [Tooltip("US cents ($50.00 = 5000).")]
        [SerializeField] private int balance = 5000;

        /// <summary>Balance in cents.</summary>
        public int Balance => balance;

        public bool CanAfford(int cents) => cents >= 0 && cents <= balance;

        /// <summary>Take <paramref name="cents"/> off the balance. False (and nothing changes) if it isn't there.</summary>
        public bool TrySpend(int cents)
        {
            if (!CanAfford(cents))
                return false;
            balance -= cents;
            return true;
        }

        /// <summary>Cents as dollars: 450 → "$4.50".</summary>
        public static string FormatUsd(int cents)
        {
            string sign = cents < 0 ? "-" : "";
            int abs = Mathf.Abs(cents);
            return $"{sign}${abs / 100:N0}.{abs % 100:00}";
        }
    }
}
