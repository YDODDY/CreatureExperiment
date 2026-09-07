using UnityEditor;
using UnityEngine;
using CreatureExperiment.Creature;

namespace CreatureExperiment.CreatureEditor
{
    /// <summary>
    /// Editor-only visualisation for <see cref="CreatureMemory"/>. Draws the live contents of the
    /// runtime <c>_observations</c> list (via the public <see cref="CreatureMemory.Observations"/>
    /// seam) as a read-only, expandable list during Play Mode.
    ///
    /// This touches nothing at runtime: no change to memory behaviour, to Record, or to the
    /// <see cref="Observation"/> data structure. It only renders what is already stored.
    /// </summary>
    [CustomEditor(typeof(CreatureMemory))]
    public class CreatureMemoryEditor : Editor
    {
        private bool _expanded = true;

        // Keep the inspector repainting each frame while playing so appended observations show up live.
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            var memory = (CreatureMemory)target;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode to inspect the stored observations.", MessageType.Info);
                return;
            }

            var observations = memory.Observations;

            EditorGUILayout.LabelField("Stored Observations", $"{observations.Count}", EditorStyles.boldLabel);

            _expanded = EditorGUILayout.Foldout(_expanded, "List<Observation> (oldest first)", true);
            if (!_expanded)
                return;

            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(true)) // read-only: this is a view, not an editor
            {
                for (int i = 0; i < observations.Count; i++)
                {
                    Observation o = observations[i];

                    EditorGUILayout.LabelField($"Element {i}", EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.ObjectField("Object", o.Object, typeof(Object), true);
                        EditorGUILayout.EnumPopup("Action", o.Action);
                        EditorGUILayout.Toggle("Contacted Creature", o.ContactedCreature);
                        EditorGUILayout.EnumPopup("Proximity", o.Proximity);
                        EditorGUILayout.FloatField("Distance From Creature", o.DistanceFromCreature);
                        EditorGUILayout.FloatField("Time", o.Time);
                    }
                }
            }
        }
    }
}
