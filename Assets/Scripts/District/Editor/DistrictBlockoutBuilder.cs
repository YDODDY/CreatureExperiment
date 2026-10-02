using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CreatureExperiment.DailyLife;

namespace CreatureExperiment.DistrictEditor
{
    /// <summary>
    /// District Blockout v0.2 (DesignReferences/DistrictBlockout_v0.2.png + _NOTES.md) for DailyLife.unity.
    ///
    /// World frame: +X = east, +Z = north (the PNG's up), street level y = -3.5. Positions follow the PNG through
    /// X = (px - 628) * 0.17, Z = (485 - py) * 0.17; roads keep realistic widths (Main Street 10 m, streets 6-7 m,
    /// alleys 5-6 m) and the beige walkable base fills the rest. Existing gameplay buildings keep their real size and are
    /// placed inside their PNG block with the PNG's entrance side.
    ///
    /// Three steps, each safe to re-run:
    /// 1. Retire Old Town   - the v0.1 strip (roads, sidewalks, ground, surroundings, old lamps, villa lot) moves under an
    ///                        inactive "Town_v0_1_Retired" root (nothing deleted; vending drink templates are kept active).
    /// 2. Move Buildings    - the gameplay roots get absolute poses (from their v0.1 poses), so running it twice is harmless:
    ///                        Villa (+ player, home spawn, home arrival, test items), Grocery Store, Game CD Shop (the old empty
    ///                        Bakery shell), Cafe, Convenience Store (the old interactive General Mart, renamed + storeId), Workplace.
    /// 3. Build District    - replaces the "District" root: base, roads, crosswalks, traffic lights, blockout buildings with
    ///                        labels and entrance markers, park / playground / open lot / parking, street furniture (clones of
    ///                        the existing lamp / tree / bench / bin / vending objects), map edge walls.
    /// Afterwards: Tools ▸ CreatureExperiment ▸ Build Day 1 Story (routes / zones follow the moved roots) and a NavMesh bake.
    /// </summary>
    public static class DistrictBlockoutBuilder
    {
        public const float Ground = -3.5f;
        private const string RetiredName = "Town_v0_1_Retired";
        private const string DistrictName = "District";
        private const string MatFolder = "Assets/Materials/District";

        // ---- Building placement (absolute; from their v0.1 identity / known poses) ---------------------------------
        public static readonly Vector3 VillaPos = new Vector3(-39.4f, 0f, 51.35f);   public const float VillaYaw = 90f;
        public static readonly Vector3 GroceryPos = new Vector3(48.7f, 0f, 63.7f);   public const float GroceryYaw = 90f;
        public static readonly Vector3 CDShopPos = new Vector3(-84.7f, 0f, -11.9f);  public const float CDShopYaw = 270f;
        public static readonly Vector3 CafePos = new Vector3(-36.1f, 0f, -64.5f);    public const float CafeYaw = 180f;
        public static readonly Vector3 StorePos = new Vector3(80f, 0f, 10.3f);       public const float StoreYaw = 90f;
        // Workplace root was at (0,0,-45): new = R(270) * (0,0,-45) + (-53, 0, -33.2)
        public static readonly Vector3 WorkplacePos = new Vector3(-8f, 0f, -33.2f);  public const float WorkplaceYaw = 270f;

        [MenuItem("Tools/CreatureExperiment/District v0.2/Build All (Retire + Move + Build)")]
        public static void BuildAll()
        {
            RetireOldTown();
            MoveBuildings();
            BuildDistrict();
        }

        // =============================================================================================================
        // 1. Retire
        // =============================================================================================================

        [MenuItem("Tools/CreatureExperiment/District v0.2/1 Retire Old Town")]
        public static void RetireOldTown()
        {
            var retired = GameObject.Find(RetiredName) ?? FindInactiveRoot(RetiredName);
            if (retired == null)
            {
                retired = new GameObject(RetiredName);
                Undo.RegisterCreatedObjectUndo(retired, "Retire Old Town");
            }

            // Keep the vending drink templates (inactive objects cloned by the vending buttons) in an active place.
            var templates = GameObject.Find("Street/Surroundings/Vending/DrinkTemplates");
            if (templates != null)
            {
                var keep = GetOrCreate(null, "DistrictTemplates_Kept");
                Undo.SetTransformParent(templates.transform, keep.transform, "Keep drink templates");
            }

            foreach (string path in new[]
            {
                "Road", "Sidewalk", "Street/Ground", "Street/Surroundings", "Street/Buildings", "Street/Entrances", "Street/Markers",
                "Villa/Structure/VillaPlaceholder", "Villa/Structure/ParkingPlaceholder",
                "Villa/Surroundings/PedestrianPaths", "Villa/Surroundings/ParkingMarkings",
            })
                Retire(path, retired.transform);

            // The old street lamps that sat under the Villa root.
            var villa = GameObject.Find("Villa");
            if (villa != null)
            {
                var lamps = new List<Transform>();
                foreach (Transform c in villa.transform)
                    if (c.name.StartsWith("StreetLight"))
                        lamps.Add(c);
                var group = GetOrCreate(retired.transform, "Villa_OldStreetLights");
                foreach (var l in lamps)
                    Undo.SetTransformParent(l, group.transform, "Retire villa lamps");
            }

            retired.SetActive(false);
            EditorSceneManager.MarkSceneDirty(retired.scene);
            Debug.Log("[District] Old town retired under " + RetiredName + " (inactive).");
        }

        private static void Retire(string path, Transform retired)
        {
            var go = GameObject.Find(path);
            if (go == null)
                return;
            string group = path.Contains("/") ? path.Substring(0, path.IndexOf('/')) : "Roots";
            var parent = GetOrCreate(retired, group);
            Undo.SetTransformParent(go.transform, parent.transform, "Retire " + path);
        }

        // =============================================================================================================
        // 2. Move gameplay buildings
        // =============================================================================================================

        [MenuItem("Tools/CreatureExperiment/District v0.2/2 Move Gameplay Buildings")]
        public static void MoveBuildings()
        {
            Quaternion villaRot = Quaternion.Euler(0f, VillaYaw, 0f);

            Place("Villa", VillaPos, VillaYaw);
            // Villa-relative objects outside the Villa root (their v0.1 poses, carried by the same move).
            PlaceRelative("Player", new Vector3(-8f, 0.16f, -1f), 0f, VillaPos, villaRot);
            PlaceRelative("HomeSpawn", new Vector3(-8f, 0.1f, -1f), 0f, VillaPos, villaRot);
            PlaceRelative("HomeArrival", new Vector3(1.3f, 1.2f, -0.95f), 0f, VillaPos, villaRot);
            Place("GarbageTestItems", VillaPos, VillaYaw); // v0.1 root at the origin, items behind the villa
            var creature = GameObject.Find("Creature_Placeholder");
            if (creature != null)
            {
                Undo.RecordObject(creature.transform, "Move creature");
                creature.transform.SetPositionAndRotation(new Vector3(88f, Ground + 1.0f, 56f), Quaternion.Euler(0f, 200f, 0f)); // in the Park
            }

            // Stores (all v0.1 roots at the origin, identity)
            Place("Street/StoreShells/GroceryStore", GroceryPos, GroceryYaw);
            RenameChildren("Street/StoreShells/GroceryStore", "Conv_", "Grocery_");
            Rename("Street/StoreShells/GroceryStore/Door_Convenience_Hinge", "Door_Grocery_Hinge");

            var cd = FindAny("Street/StoreShells/GameCDShop", "Street/StoreShells/Bakery_Shell");
            if (cd != null)
            {
                Undo.RecordObject(cd, "Rename CD shop");
                cd.name = "GameCDShop";
                RenameChildren("Street/StoreShells/GameCDShop", "Bakery_", "GameCDShop_");
                Place("Street/StoreShells/GameCDShop", CDShopPos, CDShopYaw);
            }

            Place("Street/StoreShells/Cafe_Shell", CafePos, CafeYaw);

            var store = FindAny("Street/StoreShells/ConvenienceStore", "Street/StoreShells/GeneralMart");
            if (store != null)
            {
                Undo.RecordObject(store, "Rename store");
                store.name = "ConvenienceStore";
                Place("Street/StoreShells/ConvenienceStore", StorePos, StoreYaw);
                ConvertStoreIdentity(store);
            }

            Place("Street/Workplace", WorkplacePos, WorkplaceYaw);

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[District] Gameplay buildings placed for District v0.2.");
        }

        /// <summary>Old interactive General Mart → Convenience Store: storeId on the checkout + every product template, sign text.</summary>
        private static void ConvertStoreIdentity(GameObject store)
        {
            const string id = "ConvenienceStore";
            foreach (var checkout in store.GetComponentsInChildren<CheckoutCounter>(true))
            {
                var so = new SerializedObject(checkout);
                so.FindProperty("storeId").stringValue = id;
                so.ApplyModifiedProperties();
            }
            foreach (var product in store.GetComponentsInChildren<StoreProduct>(true))
            {
                var so = new SerializedObject(product);
                so.FindProperty("storeId").stringValue = id;
                so.ApplyModifiedProperties();
            }
            var sign = store.transform.Find("Mart_Sign_Text");
            if (sign != null && sign.TryGetComponent(out TextMesh text))
            {
                Undo.RecordObject(text, "Store sign");
                if (text.text != "CONVENIENCE STORE")
                {
                    text.text = "CONVENIENCE STORE";
                    text.characterSize *= 0.72f;
                }
            }
            Rename("Street/StoreShells/ConvenienceStore/Mart_Sign_Text", "Store_Sign_Text");
            Rename("Street/StoreShells/ConvenienceStore/Mart_Sign_Board", "Store_Sign_Board");
            Rename("Street/StoreShells/ConvenienceStore/Door_GenericShop_Hinge", "Door_ConvenienceStore_Hinge");
        }

        private static void Place(string path, Vector3 pos, float yaw)
        {
            var go = GameObject.Find(path);
            if (go == null) { Debug.LogWarning("[District] missing " + path); return; }
            Undo.RecordObject(go.transform, "Place " + path);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }

        private static void PlaceRelative(string path, Vector3 oldPos, float oldYaw, Vector3 t, Quaternion r)
        {
            var go = GameObject.Find(path);
            if (go == null) { Debug.LogWarning("[District] missing " + path); return; }
            Undo.RecordObject(go.transform, "Place " + path);
            go.transform.SetPositionAndRotation(r * oldPos + t, r * Quaternion.Euler(0f, oldYaw, 0f));
        }

        // =============================================================================================================
        // 3. District blockout
        // =============================================================================================================

        private static Transform s_root;
        private static readonly Dictionary<string, Material> s_mats = new Dictionary<string, Material>();

        [MenuItem("Tools/CreatureExperiment/District v0.2/3 Build District Blockout")]
        public static void BuildDistrict()
        {
            var old = GameObject.Find(DistrictName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);
            var root = new GameObject(DistrictName);
            Undo.RegisterCreatedObjectUndo(root, "Build District");
            s_root = root.transform;
            s_mats.Clear();

            BuildGround();
            BuildRoads();
            BuildCrosswalks();
            BuildTrafficLights();
            BuildBlockoutBuildings();
            BuildGameplayEntranceMarks();
            BuildLeisure();
            BuildParking();
            BuildStreetFurniture();
            BuildEdges();

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log("[District] District v0.2 blockout built.");
        }

        // ---- Ground / roads -----------------------------------------------------------------------------------

        private const float MapX0 = -106f, MapX1 = 106f, MapZ0 = -98f, MapZ1 = 75f;

        private static void BuildGround()
        {
            var g = Group("Ground");
            // One walkable base everywhere (beige = sidewalk / walkable area); roads lie on it as flush overlays.
            var slab = Slab(g, "Base_Walkable", MapX0, MapX1, MapZ0, MapZ1, Ground - 0.3f, Ground, "Sidewalk", collider: true);
            slab.isStatic = true;
        }

        private static void BuildRoads()
        {
            var g = Group("Roads");
            // Main Street (east-west spine)
            Road(g, "MainStreet", MapX0, MapX1, -5f, 5f);
            // North streets off Main Street (to the north edge)
            Road(g, "Street_North_West (V1)", -67.35f, -60.35f, 5f, MapZ1);
            Road(g, "Street_North_East (V2)", 32.1f, 39.1f, 5f, MapZ1);
            // Alley / Back Lane behind the Villa row, and its west piece
            Road(g, "Alley_BackLane", -60.35f, 32.1f, 37f, 42f);
            Road(g, "Alley_BackLane_West", MapX0, -67.35f, 37f, 42f);
            // South side
            Road(g, "SideStreet", -49.8f, -43.8f, -58.5f, -5f);
            Road(g, "SideStreet_Stub (to facade)", -43.8f, -27.7f, -33.5f, -29.5f);
            Road(g, "BackAlley", MapX0, 20.1f, -63.5f, -58.5f);
            Road(g, "ServiceRoad_West (Conv side)", 14.1f, 20.1f, -77f, -5f);
            Road(g, "DeliveryAlley", 20.1f, 98f, -51f, -45f);
            Road(g, "ServiceLoop_East", 92f, 98f, -77f, -51f);
            Road(g, "ServiceLoop_South", 14.1f, 98f, -77f, -71f);
            // Centre dashes on Main Street
            for (float x = MapX0 + 3f; x < MapX1 - 3f; x += 6f)
            {
                if (Mathf.Abs(x + 63.85f) < 6f || Mathf.Abs(x - 35.6f) < 6f) continue; // not inside the intersections
                Strip(g, "MainStreet_Dash", x, x + 3f, -0.12f, 0.12f, "Crosswalk");
            }
        }

        private static void Road(Transform g, string name, float x0, float x1, float z0, float z1)
        {
            Slab(g, name, x0, x1, z0, z1, Ground + 0.004f, Ground + 0.012f, "Road", collider: false);
        }

        // ---- Crosswalks / traffic lights --------------------------------------------------------------------------

        // Major Main Street crossings (x centre, 3 m wide, crossing the full 10 m road) - each gets a traffic light pair.
        public static readonly float[] MajorCrossX = { -69.5f, -58.7f, 30.5f, 40.7f };

        private static void BuildCrosswalks()
        {
            var g = Group("Crosswalks");
            foreach (float x in MajorCrossX)
                CrosswalkAcrossZ(g, "Crosswalk_Major_MainStreet_X" + x.ToString("0.0"), x, -5f, 5f);
            // Minor: across side roads where they meet Main Street sidewalks, alley mouths, north ends
            CrosswalkAcrossX(g, "Crosswalk_V1_MainSidewalk", -67.35f, -60.35f, 7f);
            CrosswalkAcrossX(g, "Crosswalk_V2_MainSidewalk", 32.1f, 39.1f, 7f);
            CrosswalkAcrossX(g, "Crosswalk_V1_North", -67.35f, -60.35f, 70.5f);
            CrosswalkAcrossX(g, "Crosswalk_V2_North", 32.1f, 39.1f, 70.5f);
            CrosswalkAcrossZ(g, "Crosswalk_Alley_WestMouth", -58.8f, 37f, 42f);
            CrosswalkAcrossZ(g, "Crosswalk_Alley_EastMouth", 30.6f, 37f, 42f);
            CrosswalkAcrossZ(g, "Crosswalk_AlleyWest_V1Mouth", -68.9f, 37f, 42f);
            CrosswalkAcrossX(g, "Crosswalk_SideStreet_Main", -49.8f, -43.8f, -7f);
            CrosswalkAcrossX(g, "Crosswalk_ServiceRoad_Main", 14.1f, 20.1f, -7f);
            CrosswalkAcrossZ(g, "Crosswalk_BackAlley_East", 12.6f, -63.5f, -58.5f);
            CrosswalkAcrossZ(g, "Crosswalk_DeliveryAlley_West", 21.6f, -51f, -45f);
        }

        // Stripes along X (pedestrians walk along Z, crossing a road that runs along X).
        private static void CrosswalkAcrossZ(Transform g, string name, float xCentre, float z0, float z1)
        {
            var cw = Child(g, name);
            for (float z = z0 + 0.3f; z < z1 - 0.3f; z += 1.0f)
                Strip(cw, "Stripe", xCentre - 1.5f, xCentre + 1.5f, z, z + 0.5f, "Crosswalk");
        }

        // Stripes along Z (pedestrians walk along X, crossing a road that runs along Z).
        private static void CrosswalkAcrossX(Transform g, string name, float x0, float x1, float zCentre)
        {
            var cw = Child(g, name);
            for (float x = x0 + 0.3f; x < x1 - 0.3f; x += 1.0f)
                Strip(cw, "Stripe", x, x + 0.5f, zCentre - 1.5f, zCentre + 1.5f, "Crosswalk");
        }

        private static void BuildTrafficLights()
        {
            var g = Group("TrafficLights");
            foreach (float x in MajorCrossX)
            {
                // Off the crossing's walking line, on the outer side (away from the intersection), just behind the curb.
                bool westOfIntersection = x < -63.85f || (x > 0f && x < 35.6f);
                float lx = x + (westOfIntersection ? -2.4f : 2.4f);
                TrafficLight(g, $"TrafficLight_X{x:0.0}_North", new Vector3(lx, Ground, 5.7f), 180f);
                TrafficLight(g, $"TrafficLight_X{x:0.0}_South", new Vector3(lx, Ground, -5.7f), 0f);
            }
        }

        private static void TrafficLight(Transform g, string name, Vector3 pos, float yaw)
        {
            var tl = Child(g, name);
            tl.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            Prim(tl, PrimitiveType.Cylinder, "Pole", new Vector3(0f, 1.6f, 0f), new Vector3(0.14f, 1.6f, 0.14f), "Metal", collider: true);
            Prim(tl, PrimitiveType.Cube, "SignalBox", new Vector3(0f, 3.5f, 0.05f), new Vector3(0.36f, 1.0f, 0.3f), "Metal", collider: false);
            Prim(tl, PrimitiveType.Sphere, "Red", new Vector3(0f, 3.82f, 0.21f), Vector3.one * 0.2f, "SignalRed", collider: false);
            Prim(tl, PrimitiveType.Sphere, "Yellow", new Vector3(0f, 3.5f, 0.21f), Vector3.one * 0.2f, "SignalYellow", collider: false);
            Prim(tl, PrimitiveType.Sphere, "Green", new Vector3(0f, 3.18f, 0.21f), Vector3.one * 0.2f, "SignalGreen", collider: false);
        }

        // ---- Blockout buildings ----------------------------------------------------------------------------------

        private enum Side { North, South, East, West, None }

        private static void BuildBlockoutBuildings()
        {
            var g = Group("Buildings_Blockout");
            // Zone A / residential
            Building(g, "Blockout_Facade_01", "FACADE", -100.8f, -74.5f, 46.75f, 68f, 9f, Side.South, -87.6f, false);
            Building(g, "Blockout_Facade_02", "FACADE", 7.1f, 25.8f, 46.75f, 67.2f, 8f, Side.South, 16.4f, false);
            Building(g, "Blockout_Facade_03", "FACADE", -100.3f, -78.7f, 17f, 36.5f, 7f, Side.South, -89.4f, false);
            // Zone B / across Main Street from the villa side
            Building(g, "Blockout_GeneralMart", "GENERAL MART", 3.4f, 25.8f, 17.9f, 35.7f, 6.5f, Side.South, 14.5f, true);
            // Zone D / civic-leisure
            Building(g, "Blockout_Apartment", "APARTMENT", 45.4f, 73.4f, 48.5f, 67.1f, 16f, Side.South, 59.8f, true);
            Building(g, "Blockout_Facade_04", "FACADE", 45.4f, 59f, 17f, 38.2f, 8f, Side.South, 52.2f, false);
            // Zone C / mid commercial
            Building(g, "Blockout_Facade_05", "FACADE", -96.6f, -58.3f, -53.6f, -36.6f, 8f, Side.South, -77.9f, false);
            Building(g, "Blockout_PubBar", "PUB / BAR", -35.4f, 10.2f, -24.3f, -13.1f, 7f, Side.North, -14.5f, true);
            Building(g, "Blockout_Facade_06", "FACADE", -27.7f, 9.7f, -35.7f, -26.4f, 7f, Side.West, -31f, false);
            Building(g, "Blockout_FastFoodStore", "FAST FOOD", -36.2f, -10.7f, -53.6f, -37.4f, 6f, Side.South, -23.5f, true);
            Building(g, "Blockout_Facade_07", "FACADE", -7.8f, 9.7f, -53.6f, -37.4f, 7.5f, Side.South, 0.9f, false);
            // Zone E / service (south of the Delivery Alley)
            Building(g, "Blockout_Facade_08", "FACADE", 25.8f, 42.8f, -68f, -54f, 6.5f, Side.South, 34.3f, false);
            Building(g, "Blockout_Facade_09", "FACADE", 45.7f, 59.8f, -68f, -54f, 7f, Side.South, 52.7f, false);
            Building(g, "Blockout_Facade_10", "FACADE", 63.2f, 76f, -68f, -54f, 6f, Side.South, 69.6f, false);
        }

        private static void Building(Transform g, string name, string label, float x0, float x1, float z0, float z1, float height,
            Side front, float doorAlong, bool futureGameplay)
        {
            var b = Child(g, name);
            Box(b, "Mass", new Vector3((x0 + x1) * 0.5f, Ground + height * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, height, z1 - z0),
                futureGameplay ? "BuildingFuture" : "Building", collider: true);
            Box(b, "RoofCap", new Vector3((x0 + x1) * 0.5f, Ground + height + 0.1f, (z0 + z1) * 0.5f), new Vector3(x1 - x0 + 0.3f, 0.2f, z1 - z0 + 0.3f),
                "Roof", collider: false);
            // Roof label (readable from above) + front label over the entrance.
            Label(b, label, new Vector3((x0 + x1) * 0.5f, Ground + height + 0.25f, (z0 + z1) * 0.5f), Vector3.up, 0.6f);
            if (front != Side.None)
            {
                Entrance(b, "FrontEntrance", x0, x1, z0, z1, front, doorAlong, back: false);
                FacePose(x0, x1, z0, z1, front, doorAlong, Ground + Mathf.Min(height - 0.8f, 4.0f), 0.06f, out Vector3 facePos, out Quaternion faceRot);
                Label(b, label, facePos, faceRot * Vector3.forward, 0.4f);
            }
        }

        // Door panel on the face + a threshold plate outside it (white = front, blue = back).
        private static void Entrance(Transform b, string name, float x0, float x1, float z0, float z1, Side side, float along, bool back)
        {
            var e = Child(b, name);
            FacePose(x0, x1, z0, z1, side, along, Ground + 1.1f, 0.03f, out Vector3 doorPos, out Quaternion rot);
            e.SetPositionAndRotation(doorPos, rot);
            Box(e, "Door", e.position, Vector3.zero, "Door", collider: false, localScale: new Vector3(1.4f, 2.2f, 0.06f), local: true);
            Box(e, "Threshold", e.position, Vector3.zero, back ? "EntranceBack" : "EntranceFront", collider: false,
                localScale: new Vector3(1.8f, 0.02f, 1.2f), local: true, localPos: new Vector3(0f, -1.09f, 0.6f));
        }

        // A point on a building face (outside, offset) facing out of the building.
        private static void FacePose(float x0, float x1, float z0, float z1, Side side, float along, float y, float offset,
            out Vector3 pos, out Quaternion rot)
        {
            switch (side)
            {
                case Side.North: pos = new Vector3(along, y, z1 + offset); rot = Quaternion.Euler(0f, 0f, 0f); break;
                case Side.South: pos = new Vector3(along, y, z0 - offset); rot = Quaternion.Euler(0f, 180f, 0f); break;
                case Side.East: pos = new Vector3(x1 + offset, y, along); rot = Quaternion.Euler(0f, 90f, 0f); break;
                default: pos = new Vector3(x0 - offset, y, along); rot = Quaternion.Euler(0f, 270f, 0f); break;
            }
        }

        /// <summary>Entrance plates + labels for the gameplay buildings (they keep their real doors).</summary>
        private static void BuildGameplayEntranceMarks()
        {
            var g = Group("Buildings_GameplayMarks");
            Plate(g, "Villa_FrontEntrance_West", new Vector3(-52.5f, Ground, 45.9f), 1.8f, 1.2f, "EntranceFront");
            Plate(g, "Villa_FrontEntrance_East", new Vector3(-25.4f, Ground, 45.9f), 1.8f, 1.2f, "EntranceFront");
            Plate(g, "Grocery_FrontEntrance", new Vector3(-5.8f, Ground, 46.9f), 1.8f, 1.2f, "EntranceFront");
            Plate(g, "GameCDShop_FrontEntrance", new Vector3(-41.7f, Ground, 17.9f), 1.8f, 1.2f, "EntranceFront");
            Plate(g, "Cafe_FrontEntrance", new Vector3(-78.7f, Ground, -25.2f), 1.8f, 1.2f, "EntranceFront");
            Plate(g, "ConvenienceStore_FrontEntrance", new Vector3(52f, Ground, -19.3f), 1.8f, 1.2f, "EntranceFront");
            Plate(g, "Workplace_FrontEntrance", new Vector3(73f, Ground, -19.3f), 1.8f, 1.2f, "EntranceFront");
            Plate(g, "Workplace_BackEntrance", new Vector3(76.5f, Ground, -38.0f), 1.8f, 1.2f, "EntranceBack");
            // Labels for buildings without a sign yet
            Label(g, "GAME CD", new Vector3(-41.7f, Ground + 3.4f, 18.38f), Vector3.back, 0.45f);
            Label(g, "GAME CD", new Vector3(-41.7f, Ground + 3.4f, 25.62f), Vector3.forward, 0.45f); // seen from the villa side
            Label(g, "CAFE", new Vector3(-80f, Ground + 3.4f, -24.72f), Vector3.back, 0.45f);
            Label(g, "CAFE", new Vector3(-80f, Ground + 3.4f, -16.38f), Vector3.forward, 0.45f);      // Main Street side
            Label(g, "VILLA", new Vector3(-37.9f, Ground + 10.2f, 54.2f), Vector3.up, 0.9f);
            Label(g, "WORKPLACE · BACK", new Vector3(76.5f, Ground + 2.7f, -37.35f), Vector3.back, 0.3f);
        }

        // ---- Leisure / open spaces / parking ---------------------------------------------------------------------

        private static void BuildLeisure()
        {
            var g = Group("Leisure");
            Slab(g, "Park_Ground", 76.8f, 101.5f, 44.2f, 68.9f, Ground + 0.004f, Ground + 0.03f, "Grass", collider: false);
            Label(g, "PARK", new Vector3(89f, Ground + 0.06f, 56.5f), Vector3.up, 0.8f);
            Slab(g, "Playground_Ground", 63.2f, 101f, 15.3f, 39.9f, Ground + 0.004f, Ground + 0.03f, "Grass", collider: false);
            Slab(g, "Playground_PlayArea", 72f, 92f, 20f, 35f, Ground + 0.03f, Ground + 0.05f, "Sand", collider: false);
            Label(g, "PLAYGROUND", new Vector3(82f, Ground + 0.08f, 27.5f), Vector3.up, 0.8f);
            // Playground placeholders (simple primitives)
            Box(g, "Playground_Slide_Placeholder", new Vector3(76f, Ground + 1.0f, 31f), new Vector3(1.2f, 2f, 4f), "PlayRed", collider: true);
            Box(g, "Playground_Swing_Placeholder", new Vector3(84f, Ground + 1.25f, 32f), new Vector3(4f, 2.5f, 0.3f), "PlayBlue", collider: true);
            Box(g, "Playground_Climber_Placeholder", new Vector3(88f, Ground + 0.75f, 23f), new Vector3(3f, 1.5f, 3f), "PlayYellow", collider: true);
            Box(g, "Playground_Seesaw_Placeholder", new Vector3(78f, Ground + 0.35f, 23f), new Vector3(3.5f, 0.3f, 0.5f), "PlayBlue", collider: true);
            // Small Open Lot (mostly empty, slightly different paving)
            Slab(g, "SmallOpenLot_Paving", -28.6f, -1.4f, 17f, 36.5f, Ground + 0.004f, Ground + 0.012f, "Lot", collider: false);
            Label(g, "SMALL OPEN LOT", new Vector3(-15f, Ground + 0.03f, 26.7f), Vector3.up, 0.6f);
        }

        private static void BuildParking()
        {
            var g = Group("Parking");
            Slab(g, "Parking_Surface", -96.6f, 5.4f, -95f, -66f, Ground + 0.004f, Ground + 0.012f, "Parking", collider: false);
            for (float x = -94f; x < 4f; x += 3f)
            {
                Strip(g, "Bay_North", x, x + 0.12f, -72f, -66.5f, "Crosswalk");
                Strip(g, "Bay_South", x, x + 0.12f, -94.5f, -89f, "Crosswalk");
            }
            Label(g, "PARKING", new Vector3(-45f, Ground + 0.03f, -80.5f), Vector3.up, 1.0f);
            Box(g, "ParkedCar_Placeholder_A", new Vector3(-80.5f, Ground + 0.75f, -69.2f), new Vector3(1.8f, 1.5f, 4.4f), "Car", collider: true);
            Box(g, "ParkedCar_Placeholder_B", new Vector3(-27.5f, Ground + 0.75f, -91.8f), new Vector3(1.8f, 1.5f, 4.4f), "Car", collider: true);
        }

        // ---- Street furniture (clones of the existing objects) -----------------------------------------------------

        private static void BuildStreetFurniture()
        {
            var g = Group("StreetFurniture");
            var lampT = FindTemplate("Street/Surroundings/Lamps/StreetLight_N1");
            var treeT = FindTemplate("Street/Surroundings/Trees/StreetTree_W01");
            var benchT = FindTemplate("Street/Surroundings/Parks/ParkA/ParkA_Bench1");
            var binT = FindTemplate("Street/Surroundings/PublicBins/PublicBin_MainStreet");
            var vendT = FindTemplate("Street/Surroundings/Vending/VendingMachine");

            // Streetlamps: the arm (template local +X) is turned over the road.
            var lamps = Child(g, "Streetlamps");
            foreach (float x in new[] { -95f, -80f, -52f, -36f, -20f, -4f, 12f, 24f, 48f, 62f, 80f, 96f })
                Clone(lampT, lamps, "Lamp_Main_N", new Vector3(x, -1.07f, 5.6f), 90f);
            foreach (float x in new[] { -95f, -80f, -56f, -36f, -20f, -4f, 8f, 26f, 48f, 62f, 86f, 98f })
                Clone(lampT, lamps, "Lamp_Main_S", new Vector3(x, -1.07f, -5.6f), 270f);
            Clone(lampT, lamps, "Lamp_Villa_W", new Vector3(-48f, -1.07f, 43.2f), 90f);
            Clone(lampT, lamps, "Lamp_Villa_E", new Vector3(-30f, -1.07f, 43.2f), 90f);
            Clone(lampT, lamps, "Lamp_Grocery", new Vector3(-2f, -1.07f, 43.2f), 90f);
            Clone(lampT, lamps, "Lamp_Alley_S1", new Vector3(-20f, -1.07f, 35.8f), 270f);
            Clone(lampT, lamps, "Lamp_V1_East", new Vector3(-59.2f, -1.07f, 26f), 180f);
            Clone(lampT, lamps, "Lamp_V1_East2", new Vector3(-59.2f, -1.07f, 58f), 180f);
            Clone(lampT, lamps, "Lamp_V2_West", new Vector3(31.0f, -1.07f, 26f), 0f);
            Clone(lampT, lamps, "Lamp_V2_West2", new Vector3(31.0f, -1.07f, 58f), 0f);
            Clone(lampT, lamps, "Lamp_Cafe", new Vector3(-72f, -1.07f, -27f), 270f);
            Clone(lampT, lamps, "Lamp_SideStreet", new Vector3(-42.6f, -1.07f, -34f), 180f);
            Clone(lampT, lamps, "Lamp_BackAlley", new Vector3(-30f, -1.07f, -57.3f), 90f);
            Clone(lampT, lamps, "Lamp_Workplace_Rear", new Vector3(70f, -1.07f, -43.8f), 90f);
            Clone(lampT, lamps, "Lamp_Delivery_E", new Vector3(90.8f, -1.07f, -44f), 90f);
            Clone(lampT, lamps, "Lamp_Park", new Vector3(78f, -1.07f, 46f), 0f);
            Clone(lampT, lamps, "Lamp_Park2", new Vector3(100f, -1.07f, 66f), 180f);
            Clone(lampT, lamps, "Lamp_Playground", new Vector3(64.5f, -1.07f, 17f), 0f);
            Clone(lampT, lamps, "Lamp_Playground2", new Vector3(99.5f, -1.07f, 38f), 180f);
            Clone(lampT, lamps, "Lamp_Parking", new Vector3(-60f, -1.07f, -65f), 90f);
            Clone(lampT, lamps, "Lamp_Parking2", new Vector3(0f, -1.07f, -65f), 90f);

            var trees = Child(g, "Trees");
            foreach (var p in new[]
            {
                new Vector2(80f, 64f), new Vector2(97f, 62f), new Vector2(81f, 49f), new Vector2(97f, 48f), new Vector2(89f, 66f),  // park
                new Vector2(66f, 37f), new Vector2(99f, 18f), new Vector2(67f, 19f),                                                    // playground
                new Vector2(-85f, 9.8f), new Vector2(-44f, 9.8f), new Vector2(-2f, 9.8f), new Vector2(54f, 9.8f), new Vector2(90f, 9.8f), // Main N frontage
                new Vector2(-88f, -10.5f), new Vector2(-40f, -10.5f), new Vector2(28f, -10.5f), new Vector2(93f, -10.5f),                 // Main S frontage
                new Vector2(-21f, 44.2f), new Vector2(-58.5f, 48f), new Vector2(-61.2f, -20f), new Vector2(-101f, -45f),                  // residential / edges
                new Vector2(-20f, 30f), new Vector2(88.5f, -41.5f),                                                                        // open lot, smoking area side
            })
                Clone(treeT, trees, "Tree", new Vector3(p.x, Ground, p.y), 0f);

            var benches = Child(g, "Benches");
            Clone(benchT, benches, "Bench_Park", new Vector3(89f, Ground, 52f), 0f);
            Clone(benchT, benches, "Bench_Park2", new Vector3(92f, Ground, 61f), 180f);
            Clone(benchT, benches, "Bench_Playground", new Vector3(82f, Ground, 17.5f), 0f);
            Clone(benchT, benches, "Bench_Playground2", new Vector3(97f, Ground, 28f), 270f);
            Clone(benchT, benches, "Bench_OpenLot", new Vector3(-12f, Ground, 20f), 0f);
            Clone(benchT, benches, "Bench_Cafe_Main", new Vector3(-66f, Ground, -11f), 180f);
            Clone(benchT, benches, "Bench_Main_N", new Vector3(10f, Ground, 10.5f), 0f);

            var bins = Child(g, "TrashBins");
            foreach (var (n, p, yaw) in new[]
            {
                ("Bin_Grocery", new Vector3(-0.2f, Ground, 45.5f), 180f), ("Bin_GameCD", new Vector3(-34.5f, Ground, 16.5f), 180f),
                ("Bin_OpenLot", new Vector3(-6f, Ground, 19f), 0f), ("Bin_Park", new Vector3(86f, Ground, 50f), 0f),
                ("Bin_Playground", new Vector3(95f, Ground, 17f), 0f), ("Bin_ConvenienceStore", new Vector3(44.5f, Ground, -12f), 0f),
                ("Bin_WorkplaceRear", new Vector3(84.5f, Ground, -42.5f), 0f), ("Bin_BackAlley", new Vector3(-18f, Ground, -57.2f), 0f),
                ("Bin_Cafe", new Vector3(-86f, Ground, -26f), 180f), ("Bin_Main_N", new Vector3(-62f, Ground, 10.5f), 0f),
            })
                Clone(binT, bins, n, p, yaw);

            // Vending: sparse (residential, near the Game CD Shop, service zone). Template front faces local +X.
            var vend = Child(g, "VendingMachines");
            Clone(vendT, vend, "Vending_Residential", new Vector3(28.6f, Ground, 45.0f), 90f);   // faces south, alley corner
            Clone(vendT, vend, "Vending_GameCD", new Vector3(-50.5f, Ground, 12.0f), 90f);        // faces Main Street
            Clone(vendT, vend, "Vending_Service", new Vector3(88.5f, Ground, -12.2f), 270f);      // faces Main Street from the WP side
        }

        private static void BuildEdges()
        {
            var g = Group("MapEdge (invisible)");
            Wall(g, "Edge_W", new Vector3(MapX0 - 0.25f, Ground + 2f, (MapZ0 + MapZ1) * 0.5f), new Vector3(0.5f, 4f, MapZ1 - MapZ0));
            Wall(g, "Edge_E", new Vector3(MapX1 + 0.25f, Ground + 2f, (MapZ0 + MapZ1) * 0.5f), new Vector3(0.5f, 4f, MapZ1 - MapZ0));
            Wall(g, "Edge_S", new Vector3(0f, Ground + 2f, MapZ0 - 0.25f), new Vector3(MapX1 - MapX0, 4f, 0.5f));
            Wall(g, "Edge_N", new Vector3(0f, Ground + 2f, MapZ1 + 0.25f), new Vector3(MapX1 - MapX0, 4f, 0.5f));
        }

        private static void Wall(Transform g, string name, Vector3 c, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(g, false);
            go.transform.position = c;
            go.AddComponent<BoxCollider>().size = size;
        }

        // ---- Helpers ---------------------------------------------------------------------------------------------

        private static Transform Group(string name) => Child(s_root, name);

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static GameObject Slab(Transform parent, string name, float x0, float x1, float z0, float z1, float y0, float y1, string mat, bool collider)
            => Box(parent, name, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, y1 - y0, z1 - z0), mat, collider);

        private static void Strip(Transform parent, string name, float x0, float x1, float z0, float z1, string mat)
            => Slab(parent, name, x0, x1, z0, z1, Ground + 0.012f, Ground + 0.02f, mat, collider: false);

        private static void Plate(Transform parent, string name, Vector3 c, float w, float d, string mat)
            => Slab(parent, name, c.x - w * 0.5f, c.x + w * 0.5f, c.z - d * 0.5f, c.z + d * 0.5f, Ground + 0.012f, Ground + 0.024f, mat, collider: false);

        private static GameObject Box(Transform parent, string name, Vector3 worldCentre, Vector3 size, string mat, bool collider,
            Vector3? localScale = null, bool local = false, Vector3? localPos = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.transform.SetParent(parent, false);
            if (local)
            {
                go.transform.localPosition = localPos ?? Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = localScale ?? Vector3.one;
            }
            else
            {
                go.transform.position = worldCentre;
                go.transform.localScale = size;
            }
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(mat);
            return go;
        }

        private static void Prim(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale, string mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(mat);
        }

        /// <summary>A sign whose readable face looks along <paramref name="facing"/> (TextMesh's front is its local -Z).</summary>
        private static void Label(Transform parent, string text, Vector3 pos, Vector3 facing, float size)
        {
            var go = new GameObject("Label_" + text);
            go.transform.SetParent(parent, false);
            Quaternion rot = facing == Vector3.up
                ? Quaternion.LookRotation(Vector3.down, Vector3.forward)   // roof label, top of the text toward north
                : Quaternion.LookRotation(-facing, Vector3.up);
            go.transform.SetPositionAndRotation(pos, rot);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = size * 0.15f;
            tm.color = Color.white;
            go.AddComponent<WorldTextFont>();
        }

        private static GameObject FindTemplate(string path)
        {
            var go = GameObject.Find(path);
            if (go != null) return go;
            var retired = FindInactiveRoot(RetiredName);
            if (retired == null) return null;
            // Retired copies live under Town_v0_1_Retired/<group>/<rest of the path>
            string rest = path.Substring(path.IndexOf('/') + 1);
            string group = path.Substring(0, path.IndexOf('/'));
            var t = retired.transform.Find(group + "/" + rest);
            if (t == null) Debug.LogWarning("[District] template not found: " + path);
            return t != null ? t.gameObject : null;
        }

        private static void Clone(GameObject template, Transform parent, string name, Vector3 pos, float yaw)
        {
            if (template == null) return;
            var go = (GameObject)Object.Instantiate(template);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.SetActive(true);
        }

        private static GameObject FindAny(params string[] paths)
        {
            foreach (var p in paths)
            {
                var go = GameObject.Find(p);
                if (go != null) return go;
            }
            return null;
        }

        private static void Rename(string path, string newName)
        {
            var go = GameObject.Find(path);
            if (go == null) return;
            Undo.RecordObject(go, "Rename");
            go.name = newName;
        }

        private static void RenameChildren(string path, string from, string to)
        {
            var go = GameObject.Find(path);
            if (go == null) return;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(from))
                {
                    Undo.RecordObject(t.gameObject, "Rename");
                    t.name = to + t.name.Substring(from.Length);
                }
        }

        private static GameObject GetOrCreate(Transform parent, string name)
        {
            Transform t = parent != null ? parent.Find(name) : null;
            if (parent == null)
            {
                var found = GameObject.Find(name) ?? FindInactiveRoot(name);
                if (found != null) return found;
            }
            if (t != null) return t.gameObject;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject FindInactiveRoot(string name)
        {
            foreach (var r in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (r.name == name) return r;
            return null;
        }

        private static Material Mat(string key)
        {
            if (s_mats.TryGetValue(key, out var m) && m != null) return m;
            m = LoadMat(key);
            s_mats[key] = m;
            return m;
        }

        private static Material LoadMat(string key)
        {
            // Existing project materials first.
            string existing = key switch
            {
                "Sidewalk" => "Blockout_Sidewalk", "Road" => "Blockout_Road", "Parking" => "Blockout_Parking",
                "Building" => "Blockout_Building", "Grass" => "MAT_Grass", "Sand" => "MAT_Sand", "Metal" => "MAT_Metal_Dull",
                "PlayRed" => "MAT_Play_Red", "PlayBlue" => "MAT_Play_Blue", "PlayYellow" => "MAT_Play_Yellow",
                "Door" => "MAT_Prop_DarkPlastic", _ => null,
            };
            if (existing != null)
                foreach (string guid in AssetDatabase.FindAssets(existing + " t:Material"))
                {
                    var found = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (found != null && found.name == existing) return found;
                }

            // New flat blockout colours (Assets/Materials/District).
            Color c; bool emissive = false;
            switch (key)
            {
                case "Crosswalk": c = new Color(0.92f, 0.92f, 0.9f); break;
                case "BuildingFuture": c = new Color(0.42f, 0.36f, 0.32f); break;
                case "Roof": c = new Color(0.22f, 0.22f, 0.24f); break;
                case "Lot": c = new Color(0.86f, 0.80f, 0.66f); break;
                case "EntranceFront": c = new Color(0.95f, 0.95f, 0.95f); break;
                case "EntranceBack": c = new Color(0.25f, 0.55f, 0.95f); break;
                case "SignalRed": c = new Color(0.9f, 0.1f, 0.08f); emissive = true; break;
                case "SignalYellow": c = new Color(0.95f, 0.75f, 0.1f); emissive = true; break;
                case "SignalGreen": c = new Color(0.1f, 0.8f, 0.3f); emissive = true; break;
                case "Car": c = new Color(0.55f, 0.6f, 0.68f); break;
                default: c = Color.magenta; break;
            }
            if (!AssetDatabase.IsValidFolder(MatFolder))
                AssetDatabase.CreateFolder("Assets/Materials", "District");
            string path = $"{MatFolder}/District_{key}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", c);
            mat.color = c;
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", c * 0.6f);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
