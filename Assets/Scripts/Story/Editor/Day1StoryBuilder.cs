using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CreatureExperiment.DailyLife;
using CreatureExperiment.Story;

namespace CreatureExperiment.StoryEditor
{
    /// <summary>
    /// Builds the Day 1 story objects into the open DailyLife scene (District Blockout v0.2 layout), as one "Story" root:
    /// Day1StoryController, Mike (primitive story NPC + smoking presentation), his routes (commute, lunch into the Convenience
    /// Store's beer corner, out of it, back to work, to the smoking area, out of the scene) and the story zones (Workplace
    /// inside, 2F work area, Convenience Store inside / outside, smoking area, home entry).
    ///
    /// Points inside / at a building are given in that building's original (v0.1) frame and placed through its current root
    /// transform (<see cref="AtWorkplace"/>, <see cref="AtStore"/>, <see cref="AtVilla"/>), so they follow the building
    /// wherever it stands. Outdoor points (sidewalks, crosswalks, alleys) are District v0.2 world coordinates.
    /// Re-running replaces the previous "Story" root only; existing objects are only referenced (doors, the store's exit gate,
    /// the store cigarette templates).
    /// </summary>
    public static class Day1StoryBuilder
    {
        private const string RootName = "Story";
        private const string WorkplacePath = "Street/Workplace";
        private const string FrontDoorPath = "Street/Workplace/Workplace_Interior/Shell/Door_WorkplaceFront_Hinge";
        private const string BackDoorPath = "Street/Workplace/Workplace_Interior/Shell/Door_WorkplaceBack_Hinge";
        private const string StorePath = "Street/StoreShells/ConvenienceStore";
        private const string StoreDoorPath = "Street/StoreShells/ConvenienceStore/Door_ConvenienceStore_Hinge";
        private const string VillaPath = "Villa";
        private const float Ground = -3.5f;

        // Original (v0.1) poses of the roots the interior points were measured in.
        private static readonly Vector3 WorkplaceOriginalRootPos = new Vector3(0f, 0f, -45f);

        private static readonly Vector3 MikeStartPos = new Vector3(-55.2f, Ground, 7.6f);
        private const float MikeStartYaw = 340f;

        private static Transform s_workplace, s_store, s_villa;

        [MenuItem("Tools/CreatureExperiment/Build Day 1 Story")]
        public static void Build()
        {
            s_workplace = GameObject.Find(WorkplacePath)?.transform;
            s_store = GameObject.Find(StorePath)?.transform;
            s_villa = GameObject.Find(VillaPath)?.transform;
            if (s_workplace == null || s_store == null || s_villa == null)
            {
                Debug.LogError("[Day1StoryBuilder] Workplace / ConvenienceStore / Villa root not found - run the District v0.2 move first.");
                return;
            }

            var old = GameObject.Find(RootName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Day 1 Story");

            SwingDoor frontDoor = Door(FrontDoorPath);
            SwingDoor backDoor = Door(BackDoorPath);
            SwingDoor storeDoor = Door(StoreDoorPath);
            Transform templates = s_store.Find("ProductTemplates");
            Transform cigT = templates != null ? templates.Find("Cigarette_Template") : null;
            Transform packT = templates != null ? templates.Find("CigarettePack_Template") : null;
            StoreExitGate storeGate = s_store.GetComponentInChildren<StoreExitGate>(true);
            if (cigT == null || packT == null || storeGate == null)
                Debug.LogWarning("[Day1StoryBuilder] Convenience Store cigarette templates / exit gate not found.");

            // ---- Routes
            // Morning: from the meeting point on Main Street's north sidewalk, east, across Main Street on the V2 west
            // crosswalk, along the south sidewalk to the Workplace front, in through the front door to Mike's 1F spot.
            var commute = Child(root.transform, "MikeRoute_Day1Commute", Vector3.zero, 0f);
            Waypoint(commute, "C00_MainNorthSidewalk", new Vector3(-30f, Ground, 7f), 90f);
            Waypoint(commute, "C01_CrosswalkNorth", new Vector3(30.5f, Ground, 7f), 180f);
            Waypoint(commute, "C02_CrosswalkSouth", new Vector3(30.5f, Ground, -7f), 90f);
            Waypoint(commute, "C03_MainSouthSidewalk_Workplace", new Vector3(73f, Ground, -7f), 180f);
            Waypoint(commute, "C04_Workplace_Apron", AtWorkplace(15.0f, -126.0f), WorkplaceYaw(270f));
            Waypoint(commute, "C05_FrontDoorOutside", AtWorkplace(13.9f, -126.0f), WorkplaceYaw(270f)).openDoor = frontDoor;
            Waypoint(commute, "C06_FrontDoorInside", AtWorkplace(11.8f, -126.0f), WorkplaceYaw(270f));
            Waypoint(commute, "C07_Hall", AtWorkplace(10.4f, -126.6f), WorkplaceYaw(270f)).closeDoor = frontDoor;
            Waypoint(commute, "C08_MikeSpot", AtWorkplace(7.2f, -127.6f), WorkplaceYaw(90f)); // faces the front door

            // Lunch: out the front door, west along the frontage, into the Convenience Store, to the beer corner.
            var lunch = Child(root.transform, "MikeRoute_Day1Lunch", Vector3.zero, 0f);
            Waypoint(lunch, "L00_HallByDoor", AtWorkplace(10.6f, -126.6f), WorkplaceYaw(90f)).openDoor = frontDoor;
            Waypoint(lunch, "L01_FrontDoorInside", AtWorkplace(12.2f, -126.0f), WorkplaceYaw(90f));
            Waypoint(lunch, "L02_FrontDoorOutside", AtWorkplace(14.6f, -126.0f), WorkplaceYaw(90f)).closeDoor = frontDoor;
            Waypoint(lunch, "L03_Frontage_Workplace", new Vector3(73f, Ground, -15f), 270f);
            Waypoint(lunch, "L04_Frontage_Store", new Vector3(52f, Ground, -15f), 180f);
            Waypoint(lunch, "L05_StoreDoorOutside", AtStore(29.4f, -28.0f), StoreYaw(90f)).openDoor = storeDoor;
            Waypoint(lunch, "L06_StoreDoorInside", AtStore(31.6f, -28.3f), StoreYaw(90f));
            // Beer corner: against the wall, looking at the beer shelf, clear of the door / checkout path.
            Waypoint(lunch, "L07_StoreBeerCorner", AtStore(31.0f, -30.6f), StoreYaw(180f));

            var storeExit = Child(root.transform, "MikeRoute_Day1StoreExit", Vector3.zero, 0f);
            Waypoint(storeExit, "X00_InsideByDoor", AtStore(32.4f, -28.6f), StoreYaw(270f)).openDoor = storeDoor; // outside the leaf's swing
            Waypoint(storeExit, "X01_StoreDoorInside", AtStore(31.4f, -28.2f), StoreYaw(270f));
            Waypoint(storeExit, "X02_StoreDoorOutside", AtStore(29.4f, -28.0f), StoreYaw(270f));
            Waypoint(storeExit, "X03_OutsideSpot", new Vector3(50.5f, Ground, -16.2f), 160f); // by the door, facing it

            var back = Child(root.transform, "MikeRoute_Day1ReturnToWork", Vector3.zero, 0f);
            Waypoint(back, "R00_Frontage_Store", new Vector3(52f, Ground, -15f), 90f);
            Waypoint(back, "R01_Frontage_Workplace", new Vector3(73f, Ground, -15f), 180f);
            Waypoint(back, "R02_FrontDoorOutside", AtWorkplace(13.9f, -126.0f), WorkplaceYaw(270f)).openDoor = frontDoor;
            Waypoint(back, "R03_FrontDoorInside", AtWorkplace(11.8f, -126.0f), WorkplaceYaw(270f));
            Waypoint(back, "R04_Hall", AtWorkplace(10.4f, -126.6f), WorkplaceYaw(270f)).closeDoor = frontDoor;
            Waypoint(back, "R05_MikeSpot", AtWorkplace(7.2f, -127.6f), WorkplaceYaw(90f));

            // After work: through the hall to the Back Entrance (Delivery Alley side) and the smoking area behind it.
            var smoke = Child(root.transform, "MikeRoute_Day1Smoking", Vector3.zero, 0f);
            Waypoint(smoke, "S00_HallByBackDoor", AtWorkplace(-1.2f, -129.5f), WorkplaceYaw(270f)).openDoor = backDoor;
            Waypoint(smoke, "S01_BackDoorInside", AtWorkplace(-3.0f, -129.5f), WorkplaceYaw(270f));
            Waypoint(smoke, "S02_BackDoorOutside", AtWorkplace(-5.0f, -129.5f), WorkplaceYaw(270f)).closeDoor = backDoor;
            Waypoint(smoke, "S03_SmokingLink", AtWorkplace(-5.7f, -131.0f), WorkplaceYaw(180f));
            Waypoint(smoke, "S04_SmokingSpot", AtWorkplace(-6.0f, -132.6f), WorkplaceYaw(60f)); // by the ashtray, facing the way in

            // Mike goes home: down to the Delivery Alley, east, then south on the service loop - away from the Villa (north-west).
            var exit = Child(root.transform, "MikeRoute_Day1GoHome", Vector3.zero, 0f);
            Waypoint(exit, "E00_DeliveryAlley", new Vector3(81f, Ground, -48f), 180f);
            Waypoint(exit, "E01_DeliveryAlley_East", new Vector3(95f, Ground, -48f), 90f);
            Waypoint(exit, "E02_ServiceLoop_South", new Vector3(95f, Ground, -95f), 180f);

            // ---- Mike: where the Villa's way out meets Main Street's north sidewalk (not at the Villa door).
            // Mike waits at the V1 x Main Street corner (NE sidewalk, by the X -58.7 crosswalk's traffic light): straight
            // ahead of the player coming down the Villa's west stair along V1, facing back up the street towards them.
            var mike = BuildMike(root.transform, MikeStartPos, MikeStartYaw, commute, cigT != null ? cigT.gameObject : null, out StoryNpcSmoking smoking);

            // ---- Zones
            var zones = Child(root.transform, "Zones", Vector3.zero, 0f);
            var workplace = Zone(zones, "Zone_WorkplaceInside", AtWorkplace(9.75f, -127.7f, -2.5f), new Vector3(2.5f, 2.2f, 5.2f), s_workplace.eulerAngles.y);
            var workArea = Zone(zones, "Zone_WorkAreaInside", AtWorkplace(5.3f, -118.7f, 1.0f), new Vector3(3.4f, 2.2f, 5.0f), s_workplace.eulerAngles.y);
            var storeIn = Zone(zones, "Zone_ConvenienceStoreInside", AtStore(35.0f, -28.0f, -2.5f), new Vector3(5.2f, 2.2f, 10.6f), s_store.eulerAngles.y);
            var storeOut = Zone(zones, "Zone_ConvenienceStoreOutside", new Vector3(52f, -2.5f, -16.8f), new Vector3(6.0f, 2.2f, 4.4f), 0f);
            var smokingArea = Zone(zones, "Zone_SmokingArea", AtWorkplace(-5.75f, -132.4f, -2.5f), new Vector3(3.6f, 2.2f, 5.4f), s_workplace.eulerAngles.y);
            var home = Zone(zones, "Zone_HomeInside", AtVilla(1.0f, -0.95f, 1.0f), new Vector3(1.4f, 2.2f, 1.6f), s_villa.eulerAngles.y);
            // Narration zones: arrival (above) only records "got here"; the narration waits until the player is ~3-4 steps
            // further in. Work area: from 2.3 m past the Work Door (door at x 2.3) over the work floor. Home: the whole
            // living / dining / kitchen room minus a ~2.4 m buffer strip inside the front door (door at x 1.77).
            var workAreaNarration = Zone(zones, "Zone_WorkAreaNarration", AtWorkplace(8.75f, -122.65f, 1.0f), new Vector3(8.3f, 2.2f, 14.7f), s_workplace.eulerAngles.y);
            var homeNarration = Zone(zones, "Zone_HomeNarration", AtVilla(-1.625f, 1.675f, 1.0f), new Vector3(6.75f, 2.2f, 10.05f), s_villa.eulerAngles.y);
            var homeEntryBuffer = Zone(zones, "Zone_HomeEntryBuffer", AtVilla(0.75f, -1.05f, 1.0f), new Vector3(2.7f, 2.2f, 4.7f), s_villa.eulerAngles.y);
            var hso = new SerializedObject(homeNarration);
            hso.FindProperty("exclude").objectReferenceValue = homeEntryBuffer;
            hso.ApplyModifiedPropertiesWithoutUndo();

            // ---- Controller
            var controllerGo = Child(root.transform, "Day1StoryController", Vector3.zero, 0f).gameObject;
            var controller = Undo.AddComponent<Day1StoryController>(controllerGo);
            var so = new SerializedObject(controller);
            so.FindProperty("mike").objectReferenceValue = mike;
            so.FindProperty("mikeSmoking").objectReferenceValue = smoking;
            so.FindProperty("lunchRoute").objectReferenceValue = lunch;
            so.FindProperty("returnRoute").objectReferenceValue = back;
            so.FindProperty("storeExitRoute").objectReferenceValue = storeExit;
            so.FindProperty("smokingRoute").objectReferenceValue = smoke;
            so.FindProperty("exitRoute").objectReferenceValue = exit;
            so.FindProperty("workplaceZone").objectReferenceValue = workplace;
            so.FindProperty("workAreaZone").objectReferenceValue = workArea;
            so.FindProperty("storeInsideZone").objectReferenceValue = storeIn;
            so.FindProperty("storeOutsideZone").objectReferenceValue = storeOut;
            so.FindProperty("smokingZone").objectReferenceValue = smokingArea;
            so.FindProperty("homeZone").objectReferenceValue = home;
            so.FindProperty("workAreaNarrationZone").objectReferenceValue = workAreaNarration;
            so.FindProperty("homeNarrationZone").objectReferenceValue = homeNarration;
            so.FindProperty("storeExitGate").objectReferenceValue = storeGate;
            so.FindProperty("cigarettePackTemplate").objectReferenceValue = packT != null ? packT.gameObject : null;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log("[Day1StoryBuilder] Built 'Story' (Day1StoryController, Mike, 6 routes, 9 zones) for District v0.2.");
        }

        // ---- Building-relative points (v0.1 frame → current root) --------------------------------------------------

        private static Vector3 AtWorkplace(float x, float z, float y = Ground) =>
            s_workplace.TransformPoint(new Vector3(x, y, z) - WorkplaceOriginalRootPos);
        private static float WorkplaceYaw(float originalYaw) => s_workplace.eulerAngles.y + originalYaw;

        private static Vector3 AtStore(float x, float z, float y = Ground) => s_store.TransformPoint(new Vector3(x, y, z));
        private static float StoreYaw(float originalYaw) => s_store.eulerAngles.y + originalYaw;

        private static Vector3 AtVilla(float x, float z, float y = Ground) => s_villa.TransformPoint(new Vector3(x, y, z));

        // ---- Mike ------------------------------------------------------------------------------------------------

        private static StoryNpc BuildMike(Transform parent, Vector3 pos, float yaw, Transform route, GameObject cigTemplate, out StoryNpcSmoking smoking)
        {
            // Built at the origin (parts' positions are then both world and local), moved into place at the end.
            var mike = Child(parent, "Mike", Vector3.zero, 0f).gameObject;

            var capsule = mike.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.3f;
            var body = mike.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            Material jacket = Mat("MAT_Play_Blue");
            Material pants = Mat("MAT_Prop_DarkPlastic");
            Material skin = Mat("MAT_Cabinet_Light");
            Material hair = Mat("MAT_Bed_DarkWood");

            var visual = Child(mike.transform, "Visual", Vector3.zero, 0f);
            var legL = Child(visual, "LegPivot_L", new Vector3(-0.11f, 0.86f, 0f), 0f);
            Box(legL, "Leg_L", new Vector3(0f, -0.43f, 0f), new Vector3(0.17f, 0.86f, 0.2f), pants);
            var legR = Child(visual, "LegPivot_R", new Vector3(0.11f, 0.86f, 0f), 0f);
            Box(legR, "Leg_R", new Vector3(0f, -0.43f, 0f), new Vector3(0.17f, 0.86f, 0.2f), pants);
            Box(visual, "Body", new Vector3(0f, 1.17f, 0f), new Vector3(0.46f, 0.64f, 0.26f), jacket);
            var armL = Child(visual, "ArmPivot_L", new Vector3(-0.30f, 1.44f, 0f), 0f);
            Box(armL, "Arm_L", new Vector3(0f, -0.31f, 0f), new Vector3(0.12f, 0.62f, 0.15f), jacket);
            var armR = Child(visual, "ArmPivot_R", new Vector3(0.30f, 1.44f, 0f), 0f);
            Box(armR, "Arm_R", new Vector3(0f, -0.31f, 0f), new Vector3(0.12f, 0.62f, 0.15f), jacket);
            Box(visual, "Head", new Vector3(0f, 1.68f, 0f), new Vector3(0.22f, 0.27f, 0.24f), skin);
            Box(visual, "Hair", new Vector3(0f, 1.83f, -0.01f), new Vector3(0.24f, 0.06f, 0.26f), hair);
            var look = Child(mike.transform, "LookTarget", new Vector3(0f, 1.62f, 0f), 0f);

            var npc = mike.AddComponent<StoryNpc>();
            var so = new SerializedObject(npc);
            so.FindProperty("displayName").stringValue = "Mike";
            so.FindProperty("lookTarget").objectReferenceValue = look;
            so.FindProperty("route").objectReferenceValue = route;
            so.FindProperty("legL").objectReferenceValue = legL;
            so.FindProperty("legR").objectReferenceValue = legR;
            so.FindProperty("armL").objectReferenceValue = armL;
            so.FindProperty("armR").objectReferenceValue = armR;
            so.ApplyModifiedPropertiesWithoutUndo();

            smoking = mike.AddComponent<StoryNpcSmoking>();
            var sso = new SerializedObject(smoking);
            sso.FindProperty("visualRoot").objectReferenceValue = visual;
            sso.FindProperty("armR").objectReferenceValue = armR;
            sso.FindProperty("armL").objectReferenceValue = armL;
            sso.FindProperty("cigaretteTemplate").objectReferenceValue = cigTemplate;
            sso.ApplyModifiedPropertiesWithoutUndo();

            mike.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            return npc;
        }

        // ---- Helpers ---------------------------------------------------------------------------------------------

        private static SwingDoor Door(string path)
        {
            var go = GameObject.Find(path);
            SwingDoor door = go != null ? go.GetComponent<SwingDoor>() : null;
            if (door == null)
                Debug.LogWarning($"[Day1StoryBuilder] Door not found: {path} - Mike's route won't open it.");
            return door;
        }

        private static Transform Child(Transform parent, string name, Vector3 worldPos, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(worldPos, Quaternion.Euler(0f, yaw, 0f));
            return go.transform;
        }

        private static StoryWaypoint Waypoint(Transform route, string name, Vector3 pos, float yaw) =>
            Child(route, name, pos, yaw).gameObject.AddComponent<StoryWaypoint>();

        private static StoryZone Zone(Transform parent, string name, Vector3 center, Vector3 size, float yaw)
        {
            var t = Child(parent, name, center, yaw);
            t.localScale = size;
            return t.gameObject.AddComponent<StoryZone>();
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
            if (mat != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Material Mat(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets(name + " t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.name == name)
                    return m;
            }
            Debug.LogWarning($"[Day1StoryBuilder] Material {name} not found.");
            return null;
        }
    }
}
