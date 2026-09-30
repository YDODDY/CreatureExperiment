using System.Collections.Generic;
using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Marks a leftover of everyday life that is gone by the next day - a drink spill, an egg / jam / butter stain.
    /// Put it on the (inactive) template; every copy registers itself when it comes alive and
    /// <see cref="DailyMessCleanup"/> destroys exactly the registered ones when a new day starts. Nothing else is
    /// searched for or touched. The inactive template itself never registers, so it survives.
    /// </summary>
    public class TemporaryMess : MonoBehaviour
    {
        private static readonly HashSet<TemporaryMess> s_all = new HashSet<TemporaryMess>();
        private static readonly List<TemporaryMess> s_buffer = new List<TemporaryMess>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_all.Clear();

        private void Awake() => s_all.Add(this);
        private void OnDestroy() => s_all.Remove(this);

        /// <summary>Destroy every live temporary mess. Returns how many.</summary>
        public static int ClearAll()
        {
            s_buffer.Clear();
            s_buffer.AddRange(s_all);
            int n = 0;
            foreach (TemporaryMess mess in s_buffer)
            {
                if (mess == null)
                    continue;
                Destroy(mess.gameObject);
                n++;
            }
            s_all.Clear();
            return n;
        }
    }
}
