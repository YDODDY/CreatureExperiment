using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CreatureExperiment.DailyLifeEditor
{
    /// <summary>
    /// Functional ceiling lights for the Workplace interior, so the routes and interaction spots read at night (the
    /// Workplace had no lights; LightingEnvironment only changes sun / ambient / street lamps, so these stay on in both
    /// Day and Night). Long fluorescent-style fixtures (MAT_Light_Fixture body, MAT_Light_On_Cool lens) with a cool point
    /// light each, no shadows - the same style as the Villa corridor lights.
    /// Builds "InteriorLighting" under Street/Workplace/Workplace_Interior; re-running replaces only that object.
    /// </summary>
    public static class WorkplaceLightingBuilder
    {
        private const string ParentPath = "Street/Workplace/Workplace_Interior";
        private const string RootName = "InteriorLighting";
        private const float F1Ceiling = -0.3f; // underside of the 2F slab
        private const float F2Ceiling = 3.5f;  // underside of the roof
        private static readonly Color LightColor = new Color(0.92f, 0.96f, 1f);

        private struct Spot
        {
            public string Name; public float X, Z, Ceiling, Intensity, Range; public bool AlongZ;
            public Spot(string name, float x, float z, float ceiling, float intensity, float range, bool alongZ = false)
            { Name = name; X = x; Z = z; Ceiling = ceiling; Intensity = intensity; Range = range; AlongZ = alongZ; }
        }

        private static readonly Spot[] Spots =
        {
            // 1F
            new Spot("F1_Entrance", 11.3f, -127.6f, F1Ceiling, 2.6f, 6f),
            new Spot("F1_Reception", 11.4f, -133.8f, F1Ceiling, 2.2f, 5.5f),
            new Spot("F1_HallCenter", 5.0f, -127.7f, F1Ceiling, 2.6f, 7f),
            new Spot("F1_HallWest_BackDoor", -1.8f, -129.2f, F1Ceiling, 2.4f, 6f),
            new Spot("F1_StairBottom", -2.7f, -123.6f, F1Ceiling, 2.2f, 5f),
            new Spot("F1_BreakRoom", 2.6f, -120.0f, F1Ceiling, 2.0f, 5.5f),
            new Spot("F1_LockerRoom", 9.6f, -120.0f, F1Ceiling, 2.0f, 5.5f),
            // Stairwell (open up to the roof) and the 2F stair exit
            new Spot("Stair_Mid", -3.1f, -120.0f, F2Ceiling, 3.0f, 8f, alongZ: true),
            new Spot("Stair_Top", -2.6f, -116.4f, F2Ceiling, 2.2f, 5f),
            // 2F
            new Spot("F2_Prep_Readers", 0.2f, -119.6f, F2Ceiling, 2.6f, 6f),
            new Spot("F2_WorkEntrance", 4.6f, -118.7f, F2Ceiling, 2.6f, 6f),
            new Spot("F2_WorkTable", 8.0f, -117.4f, F2Ceiling, 2.8f, 7f),
            new Spot("F2_Receivers", 10.6f, -122.6f, F2Ceiling, 2.6f, 6.5f),
            new Spot("F2_WorkSouth", 5.0f, -129.5f, F2Ceiling, 2.4f, 8.5f),
        };

        [MenuItem("Tools/CreatureExperiment/Build Workplace Interior Lighting")]
        public static void Build()
        {
            var parent = GameObject.Find(ParentPath);
            if (parent == null)
            {
                Debug.LogError($"[WorkplaceLightingBuilder] {ParentPath} not found.");
                return;
            }
            var old = parent.transform.Find(RootName);
            if (old != null)
                Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Workplace Interior Lighting");
            root.transform.SetParent(parent.transform, false);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            Material body = Mat("MAT_Light_Fixture");
            Material lens = Mat("MAT_Light_On_Cool");

            foreach (var s in Spots)
            {
                var fixture = new GameObject("CeilingLight_" + s.Name);
                fixture.transform.SetParent(root.transform, false);
                fixture.transform.SetPositionAndRotation(new Vector3(s.X, s.Ceiling, s.Z), Quaternion.Euler(0f, s.AlongZ ? 90f : 0f, 0f));

                Box(fixture.transform, "Fixture_Body", new Vector3(0f, -0.025f, 0f), new Vector3(1.2f, 0.05f, 0.28f), body);
                Box(fixture.transform, "Fixture_Lens", new Vector3(0f, -0.055f, 0f), new Vector3(1.1f, 0.012f, 0.2f), lens);

                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(fixture.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, -0.2f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = LightColor;
                light.intensity = s.Intensity;
                light.range = s.Range;
                light.shadows = LightShadows.None;
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[WorkplaceLightingBuilder] Built {Spots.Length} Workplace interior lights.");
        }

        private static void Box(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (mat != null)
                r.sharedMaterial = mat;
        }

        private static Material Mat(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets(name + " t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.name == name)
                    return m;
            }
            Debug.LogWarning($"[WorkplaceLightingBuilder] Material {name} not found.");
            return null;
        }
    }
}
