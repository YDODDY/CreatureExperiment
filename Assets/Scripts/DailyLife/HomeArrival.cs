using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A trigger volume just inside the house entrance. When the player walks back in it tells
    /// <see cref="DailyLifeDirector"/>, which flips CommuteHome -> Sleep. Purely arrival detection -
    /// it never moves the player and does nothing outside the CommuteHome phase.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HomeArrival : MonoBehaviour
    {
        [SerializeField] private DailyLifeDirector director;
        [Tooltip("Tag that identifies the player object.")]
        [SerializeField] private string playerTag = "Player";

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (director == null)
                return;
            if (!string.IsNullOrEmpty(playerTag) && !other.CompareTag(playerTag))
                return;
            director.OnEnteredHome();
        }
    }
}
