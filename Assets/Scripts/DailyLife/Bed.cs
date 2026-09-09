using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The home bed. Using it ends the day - <see cref="DailyLifeDirector"/> gates that on being in
    /// the Sleep phase (clocked out and back inside the house), so it does nothing at any other time.
    /// Not a pickup - no <c>Interactable</c>.
    /// </summary>
    public class Bed : MonoBehaviour, IUsable
    {
        [SerializeField] private DailyLifeDirector director;

        public void Use()
        {
            if (director != null)
                director.UseBed();
        }
    }
}
