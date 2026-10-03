using UnityEditor;
using UnityEngine;
using CreatureExperiment.DailyLife;

namespace CreatureExperiment.DistrictEditor
{
    /// <summary>
    /// One set of bathroom interactions for every toilet / sink in the project (the Player Room's and the pub's) -
    /// the same components, the same layout rules, no scene-specific code:
    /// - Toilet: the body collider stops under the seat (a separate tank collider), a hinged lid + seat
    ///   (<see cref="HingeToggle"/>, E = up / down) and a flush lever on the tank's left side (<see cref="ToiletFlush"/> on
    ///   the lever's own collider, E = flush; it animates the bowl water). Each part is its own aim target.
    /// - Faucet: a tap with its own collider carrying <see cref="WaterToggle"/> (E = water on / off) and a stream visual.
    /// Re-applying is safe: earlier kit parts are replaced.
    /// </summary>
    public static class BathroomKit
    {
        public struct ToiletShape
        {
            public float BowlTop;     // local y of the bowl rim
            public float PivotZ;      // local z of the lid hinge (~4 cm in front of the tank face, so the raised lid stays aimable)
            public float TankFrontZ;
            public float TankTop;
            public float TankZ;       // tank centre z
            public float TankDepth;
        }

        [MenuItem("Tools/CreatureExperiment/Eateries/Upgrade Player Room Bathroom (toilet kit)")]
        public static void UpgradePlayerRoomBathroom()
        {
            var toilet = GameObject.Find("Villa/Structure/Floor2/Home/Bathroom/Toilet");
            if (toilet == null)
            {
                Debug.LogError("[BathroomKit] Player Room toilet not found");
                return;
            }
            Material porcelain = toilet.transform.Find("Tank")?.GetComponent<Renderer>()?.sharedMaterial;
            UpgradeToilet(toilet.transform,
                new ToiletShape { BowlTop = 0.42f, PivotZ = 0.12f, TankFrontZ = 0.16f, TankTop = 0.78f, TankZ = 0.25f, TankDepth = 0.18f },
                porcelain, Mat("MAT_Metal_Dull"), Mat("MAT_Water_Simple"));
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(toilet.scene);
        }

        public static void UpgradeToilet(Transform toilet, ToiletShape s, Material lidMat, Material chrome, Material water)
        {
            // Old single-target setup: flush on the whole toilet, a fixed lid / seat.
            Transform bowlWater = toilet.Find("BowlWater");
            var oldFlush = toilet.GetComponent<ToiletFlush>();
            if (oldFlush != null)
            {
                var so = new SerializedObject(oldFlush);
                if (so.FindProperty("bowlWater").objectReferenceValue is Transform w)
                    bowlWater = w;
                Object.DestroyImmediate(oldFlush);
            }
            foreach (var n in new[] { "Lid", "Seat", "LidPivot", "Flush", "FlushButton", "TankCollider" })
            {
                var c = toilet.Find(n);
                if (c != null)
                    Object.DestroyImmediate(c.gameObject);
            }
            if (bowlWater == null)
                bowlWater = Prim(toilet, PrimitiveType.Cylinder, "BowlWater", new Vector3(0f, s.BowlTop + 0.004f, -0.12f),
                    new Vector3(0.28f, 0.002f, 0.3f), water).transform;

            // Body collider only up to the rim; the tank gets its own (no Use on either - they are plain solids).
            var body = toilet.GetComponent<BoxCollider>();
            if (body == null)
                body = toilet.gameObject.AddComponent<BoxCollider>();
            float bodyTop = s.BowlTop - 0.02f;
            body.size = new Vector3(0.42f, bodyTop, Mathf.Max(body.size.z, 0.6f));
            body.center = new Vector3(0f, bodyTop * 0.5f, body.center.z);
            var tank = new GameObject("TankCollider").transform;
            tank.SetParent(toilet, false);
            tank.localPosition = new Vector3(0f, (bodyTop + s.TankTop) * 0.5f, s.TankZ);
            tank.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.4f, s.TankTop - bodyTop, s.TankDepth + 0.02f);

            // Lid + seat on one hinge (E: up / down). Lies over the bowl when down.
            var pivot = new GameObject("LidPivot").transform;
            pivot.SetParent(toilet, false);
            pivot.localPosition = new Vector3(0f, s.BowlTop + 0.008f, s.PivotZ);
            float len = 0.47f;
            Prim(pivot, PrimitiveType.Cylinder, "Seat", new Vector3(0f, 0.004f, -len * 0.5f), new Vector3(0.41f, 0.005f, len), lidMat);
            var lid = Prim(pivot, PrimitiveType.Cylinder, "Lid", new Vector3(0f, 0.018f, -len * 0.5f + 0.005f), new Vector3(0.4f, 0.008f, len - 0.01f), lidMat);
            lid.AddComponent<BoxCollider>().size = new Vector3(1f, 6f, 1f); // ~5 cm thick: raised, it stands proud of the tank's collider
            foreach (float x in new[] { -0.12f, 0.12f })
                Prim(pivot, PrimitiveType.Cylinder, "HingeCap", new Vector3(x, 0.01f, 0f), new Vector3(0.03f, 0.02f, 0.03f), chrome, new Vector3(0f, 0f, 90f));
            var toggle = pivot.gameObject.AddComponent<HingeToggle>();
            Set(toggle, "closedEuler", Vector3.zero);
            Set(toggle, "openEuler", new Vector3(92f, 0f, 0f));

            // Flush lever on the tank's left front corner (E: flush) - clear of the raised lid.
            var flush = new GameObject("Flush").transform;
            flush.SetParent(toilet, false);
            // In front of / beside the tank so the aim meets the lever before the tank's own collider (and beside the raised lid).
            flush.localPosition = new Vector3(-0.235f, s.TankTop - 0.07f, s.TankFrontZ - 0.02f);
            var fc = flush.gameObject.AddComponent<BoxCollider>();
            fc.size = new Vector3(0.14f, 0.08f, 0.08f);
            fc.center = new Vector3(0.0f, 0f, -0.01f);
            Prim(flush, PrimitiveType.Cylinder, "LeverHub", new Vector3(0.02f, 0f, 0f), new Vector3(0.035f, 0.012f, 0.035f), chrome, new Vector3(0f, 0f, 90f));
            var arm = Prim(flush, PrimitiveType.Cube, "LeverArm", new Vector3(-0.03f, 0f, -0.015f), new Vector3(0.1f, 0.018f, 0.025f), chrome);
            var flushComp = flush.gameObject.AddComponent<ToiletFlush>();
            Set(flushComp, "bowlWater", bowlWater);
            Set(flushComp, "flushButton", arm.transform);
            Set(flushComp, "buttonTravel", 0.02f);
            Set(flushComp, "buttonTime", 0.35f);
        }

        /// <summary>A tap with its own aim target (E: water on / off). <paramref name="basePos"/> = foot of the column, the
        /// spout points along <paramref name="spoutDir"/> (world, horizontal), the stream falls to <paramref name="streamBottomY"/>.</summary>
        public static GameObject Faucet(Transform parent, string name, Vector3 basePos, Vector3 spoutDir, float columnHeight, float spoutLength,
            float streamBottomY, Material chrome, Material water)
        {
            var tap = new GameObject(name).transform;
            tap.SetParent(parent, false);
            tap.SetPositionAndRotation(basePos, Quaternion.LookRotation(spoutDir, Vector3.up));
            Prim(tap, PrimitiveType.Cylinder, "Column", new Vector3(0f, columnHeight * 0.5f, 0f), new Vector3(0.035f, columnHeight * 0.5f, 0.035f), chrome);
            Prim(tap, PrimitiveType.Cube, "Spout", new Vector3(0f, columnHeight - 0.015f, spoutLength * 0.5f), new Vector3(0.03f, 0.03f, spoutLength), chrome);
            Prim(tap, PrimitiveType.Cylinder, "Handle", new Vector3(0f, columnHeight + 0.015f, -0.01f), new Vector3(0.05f, 0.012f, 0.05f), chrome);
            float top = columnHeight - 0.03f, bottom = streamBottomY - basePos.y;
            var stream = Prim(tap, PrimitiveType.Cylinder, "WaterVisual", new Vector3(0f, (top + bottom) * 0.5f, spoutLength - 0.012f),
                new Vector3(0.018f, (top - bottom) * 0.5f, 0.018f), water);
            stream.SetActive(false);
            var col = tap.gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(0.1f, columnHeight + 0.05f, spoutLength + 0.06f);
            col.center = new Vector3(0f, (columnHeight + 0.05f) * 0.5f, spoutLength * 0.5f - 0.01f);
            var toggle = tap.gameObject.AddComponent<WaterToggle>();
            Set(toggle, "waterVisual", stream);
            return tap.gameObject;
        }

        // ---- helpers ------------------------------------------------------------------------------------------------

        private static GameObject Prim(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale, Material mat,
            Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = localScale;
            if (mat != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static Material Mat(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets(name + " t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.name == name)
                    return m;
            }
            return null;
        }

        private static void Set(Object target, string prop, object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(prop);
            if (p == null)
            {
                Debug.LogError($"[BathroomKit] {target.GetType().Name}.{prop} not found");
                return;
            }
            switch (value)
            {
                case float f: p.floatValue = f; break;
                case Vector3 v: p.vector3Value = v; break;
                case Object o: p.objectReferenceValue = o; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
