using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The morning clean: when the <see cref="DailyLifeDirector"/> starts a new day, every <see cref="TemporaryMess"/>
    /// (drink spills, egg / jam / butter stains) made so far is removed. Only objects that carry TemporaryMess.
    /// </summary>
    public class DailyMessCleanup : MonoBehaviour
    {
        [Tooltip("Found on this GameObject / in the scene if empty.")]
        [SerializeField] private DailyLifeDirector director;

        private void Awake()
        {
            if (director == null && !TryGetComponent(out director))
                director = FindFirstObjectByType<DailyLifeDirector>();
        }

        private void OnEnable()
        {
            if (director != null)
                director.DayStarted += OnDayStarted;
        }

        private void OnDisable()
        {
            if (director != null)
                director.DayStarted -= OnDayStarted;
        }

        private void OnDayStarted(int day)
        {
            int n = TemporaryMess.ClearAll();
            if (n > 0)
                Debug.Log($"[DailyMessCleanup] Day {day}: removed {n} temporary mess object(s).");
        }
    }
}
