using System.Collections.Generic;
using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Puts every gameplay door back to closed when a new day starts. The single door-closing entry
    /// point: it listens to <see cref="DailyLifeDirector.DayStarted"/> once and snaps each door in
    /// <see cref="managedDoors"/> shut - the WorkArea security door included.
    ///
    /// It never touches locks: the WorkArea door's lock stays owned by <see cref="WorkplaceAttendance"/>
    /// (locked on the same day start, unlocked by clock-in). It reads no input.
    ///
    /// The day rolls over from the Bed after the player has been teleported home, so no character is
    /// standing in a doorway when the doors snap shut.
    /// </summary>
    public class DoorDayResetManager : MonoBehaviour
    {
        [SerializeField] private DailyLifeDirector director;
        [Tooltip("Gameplay doors closed on every new day. Leave out test / legacy doors.")]
        [SerializeField] private List<SwingDoor> managedDoors = new List<SwingDoor>();

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
            int closed = 0;
            for (int i = 0; i < managedDoors.Count; i++)
            {
                SwingDoor door = managedDoors[i];
                if (door == null)
                    continue;
                door.SnapClosed();
                closed++;
            }
            Debug.Log($"[DoorDayReset] Day {day}: {closed} door(s) closed.");
        }
    }
}
