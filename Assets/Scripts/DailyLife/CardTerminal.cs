using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// LEGACY (TestWorld_Old, now inactive). The old single wall card reader; replaced by the
    /// Workplace's <see cref="WorkplaceCardReader"/>s + <see cref="WorkplaceAttendance"/>.
    /// <see cref="DailyLifeDirector.UseTerminal"/> now ignores it. Not a pickup - no <c>Interactable</c>.
    /// </summary>
    public class CardTerminal : MonoBehaviour, IUsable
    {
        [SerializeField] private DailyLifeDirector director;

        public void Use()
        {
            if (director != null)
                director.UseTerminal();
        }
    }
}
