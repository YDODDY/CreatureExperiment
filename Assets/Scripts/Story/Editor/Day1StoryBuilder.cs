using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CreatureExperiment.DailyLife;
using CreatureExperiment.Story;

namespace CreatureExperiment.StoryEditor
{
    /// <summary>
    /// Builds the Day 1 story objects into the open DailyLife scene, as one "Story" root:
    /// Day1StoryController, Mike (primitive story NPC + smoking presentation, waiting on the sidewalk where the Villa's south
    /// path meets it), his routes (commute, lunch into the General Mart's beer corner, out of it, back to work, to the smoking
    /// area, out of the scene) and the
    /// story zones (Workplace inside, 2F work area, General Mart inside / outside, smoking area, home entry).
    /// Re-running replaces the previous "Story" root only. Nothing else in the scene is moved; existing objects are only
    /// referenced (doors, the mart's exit gate, the store cigarette templates).
    /// Positions come from the live scene survey; move the objects in the scene to tune them (a rebuild resets them).
    /// </summary>
    public static class Day1StoryBuilder
    {
        private const string RootName = "Story";
        private const string FrontDoorPath = "Street/Workplace/Workplace_Interior/Shell/Door_WorkplaceFront_Hinge";
        private const string BackDoorPath = "Street/Workplace/Workplace_Interior/Shell/Door_WorkplaceBack_Hinge";
        private const string MartPath = "Street/StoreShells/GeneralMart";

        [MenuItem("Tools/CreatureExperiment/Build Day 1 Story")]
        public static void Build()
        {
            var old = GameObject.Find(RootName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Day 1 Story");

            SwingDoor frontDoor = Door(FrontDoorPath);
            SwingDoor backDoor = Door(BackDoorPath);
            var mart = GameObject.Find(MartPath);
            Transform templates = mart != null ? mart.transform.Find("ProductTemplates") : null;
            Transform cigT = templates != null ? templates.Find("Cigarette_Template") : null;
            Transform packT = templates != null ? templates.Find("CigarettePack_Template") : null;
            StoreExitGate martGate = mart != null ? mart.GetComponentInChildren<StoreExitGate>(true) : null;
            SwingDoor martDoor = mart != null ? mart.transform.Find("Door_GenericShop_Hinge")?.GetComponent<SwingDoor>() : null;
            if (cigT == null || packT == null || martGate == null)
                Debug.LogWarning("[Day1StoryBuilder] General Mart cigarette templates / exit gate not found.");

            // ---- Routes
            var commute = Child(root.transform, "MikeRoute_Day1Commute", Vector3.zero, 0f);
            Waypoint(commute, "WP00_SidewalkStart", new Vector3(18.2f, -3.5f, -16.5f), 180f);
            Waypoint(commute, "WP01_SidewalkWorkplace", new Vector3(18.2f, -3.5f, -124.6f), 180f);
            Waypoint(commute, "WP02_Apron", new Vector3(15.0f, -3.5f, -126.0f), 270f);
            Waypoint(commute, "WP03_FrontDoorOutside", new Vector3(13.9f, -3.5f, -126.0f), 270f).openDoor = frontDoor;
            Waypoint(commute, "WP04_FrontDoorInside", new Vector3(11.8f, -3.5f, -126.0f), 270f);
            Waypoint(commute, "WP05_Hall", new Vector3(10.4f, -3.5f, -126.6f), 270f).closeDoor = frontDoor;
            Waypoint(commute, "WP06_MikeSpot", new Vector3(7.2f, -3.5f, -127.6f), 90f); // faces the front door

            var lunch = Child(root.transform, "MikeRoute_Day1Lunch", Vector3.zero, 0f);
            Waypoint(lunch, "L00_HallByDoor", new Vector3(10.6f, -3.5f, -126.6f), 90f).openDoor = frontDoor;
            Waypoint(lunch, "L01_FrontDoorInside", new Vector3(12.2f, -3.5f, -126.0f), 90f);
            Waypoint(lunch, "L02_FrontDoorOutside", new Vector3(14.6f, -3.5f, -126.0f), 90f).closeDoor = frontDoor;
            Waypoint(lunch, "L03_Sidewalk", new Vector3(18.2f, -3.5f, -124.6f), 0f);
            Waypoint(lunch, "L04_SidewalkCrosswalk", new Vector3(18.4f, -3.5f, -36.25f), 90f);
            Waypoint(lunch, "L05_OppositeSidewalk", new Vector3(27.8f, -3.5f, -36.25f), 0f);
            Waypoint(lunch, "L06_MartDoorOutside", new Vector3(29.4f, -3.5f, -28.0f), 90f).openDoor = martDoor;
            Waypoint(lunch, "L07_MartDoorInside", new Vector3(31.6f, -3.5f, -28.3f), 90f);
            // Beer corner, against the west wall: looking at the beer, 2.4 m off the shelves, clear of the door / checkout path.
            Waypoint(lunch, "L08_MartBeerCorner", new Vector3(31.0f, -3.5f, -30.6f), 180f);

            var martExit = Child(root.transform, "MikeRoute_Day1MartExit", Vector3.zero, 0f);
            Waypoint(martExit, "X00_InsideByDoor", new Vector3(32.4f, -3.5f, -28.6f), 270f).openDoor = martDoor; // outside the leaf's swing
            Waypoint(martExit, "X01_MartDoorInside", new Vector3(31.4f, -3.5f, -28.2f), 270f);
            Waypoint(martExit, "X02_MartDoorOutside", new Vector3(29.4f, -3.5f, -28.0f), 270f);
            Waypoint(martExit, "X03_OutsideSpot", new Vector3(28.4f, -3.5f, -30.0f), 47f); // by the door, facing it

            var back = Child(root.transform, "MikeRoute_Day1ReturnToWork", Vector3.zero, 0f);
            Waypoint(back, "R00_OppositeSidewalk", new Vector3(27.8f, -3.5f, -36.25f), 270f);
            Waypoint(back, "R01_SidewalkCrosswalk", new Vector3(18.4f, -3.5f, -36.25f), 180f);
            Waypoint(back, "R02_Sidewalk", new Vector3(18.2f, -3.5f, -40.0f), 180f);
            Waypoint(back, "R03_SidewalkWorkplace", new Vector3(18.2f, -3.5f, -124.6f), 180f);
            Waypoint(back, "R04_Apron", new Vector3(15.0f, -3.5f, -126.0f), 270f);
            Waypoint(back, "R05_FrontDoorOutside", new Vector3(13.9f, -3.5f, -126.0f), 270f).openDoor = frontDoor;
            Waypoint(back, "R06_FrontDoorInside", new Vector3(11.8f, -3.5f, -126.0f), 270f);
            Waypoint(back, "R07_Hall", new Vector3(10.4f, -3.5f, -126.6f), 270f).closeDoor = frontDoor;
            Waypoint(back, "R08_MikeSpot", new Vector3(7.2f, -3.5f, -127.6f), 90f);

            var smoke = Child(root.transform, "MikeRoute_Day1Smoking", Vector3.zero, 0f);
            Waypoint(smoke, "S00_HallByBackDoor", new Vector3(-1.2f, -3.5f, -129.5f), 270f).openDoor = backDoor;
            Waypoint(smoke, "S01_BackDoorInside", new Vector3(-3.0f, -3.5f, -129.5f), 270f);
            Waypoint(smoke, "S02_BackDoorOutside", new Vector3(-5.0f, -3.5f, -129.5f), 270f).closeDoor = backDoor;
            Waypoint(smoke, "S03_SmokingLink", new Vector3(-5.7f, -3.5f, -131.0f), 180f);
            Waypoint(smoke, "S04_SmokingSpot", new Vector3(-6.0f, -3.5f, -132.6f), 60f); // by the ashtray, facing the way in

            var exit = Child(root.transform, "MikeRoute_Day1GoHome", Vector3.zero, 0f);
            Waypoint(exit, "E00_BehindWorkplace", new Vector3(-6.0f, -3.5f, -138.6f), 180f);
            Waypoint(exit, "E01_SouthStrip", new Vector3(16.0f, -3.5f, -138.6f), 90f);
            Waypoint(exit, "E02_Sidewalk", new Vector3(18.2f, -3.5f, -141.0f), 180f);
            Waypoint(exit, "E03_SidewalkSouth", new Vector3(18.2f, -3.5f, -190.0f), 180f); // away from the Villa (north)

            // ---- Mike
            var mike = BuildMike(root.transform, new Vector3(17.9f, -3.5f, -14.6f), 270f, commute,
                cigT != null ? cigT.gameObject : null, out StoryNpcSmoking smoking);

            // ---- Zones
            var zones = Child(root.transform, "Zones", Vector3.zero, 0f);
            var workplace = Zone(zones, "Zone_WorkplaceInside", new Vector3(9.75f, -2.5f, -127.7f), new Vector3(2.5f, 2.2f, 5.2f));
            var workArea = Zone(zones, "Zone_WorkAreaInside", new Vector3(5.3f, 1.0f, -118.7f), new Vector3(3.4f, 2.2f, 5.0f));
            var martIn = Zone(zones, "Zone_MartInside", new Vector3(35.0f, -2.5f, -28.0f), new Vector3(5.2f, 2.2f, 10.6f));
            var martOut = Zone(zones, "Zone_MartOutside", new Vector3(28.6f, -2.5f, -28.0f), new Vector3(3.2f, 2.2f, 5.0f));
            var smokingArea = Zone(zones, "Zone_SmokingArea", new Vector3(-5.75f, -2.5f, -132.4f), new Vector3(3.6f, 2.2f, 5.4f));
            var home = Zone(zones, "Zone_HomeInside", new Vector3(1.0f, 1.0f, -0.95f), new Vector3(1.4f, 2.2f, 1.6f));

            // ---- Controller
            var controllerGo = Child(root.transform, "Day1StoryController", Vector3.zero, 0f).gameObject;
            var controller = Undo.AddComponent<Day1StoryController>(controllerGo);
            var so = new SerializedObject(controller);
            so.FindProperty("mike").objectReferenceValue = mike;
            so.FindProperty("mikeSmoking").objectReferenceValue = smoking;
            so.FindProperty("lunchRoute").objectReferenceValue = lunch;
            so.FindProperty("returnRoute").objectReferenceValue = back;
            so.FindProperty("martExitRoute").objectReferenceValue = martExit;
            so.FindProperty("smokingRoute").objectReferenceValue = smoke;
            so.FindProperty("exitRoute").objectReferenceValue = exit;
            so.FindProperty("workplaceZone").objectReferenceValue = workplace;
            so.FindProperty("workAreaZone").objectReferenceValue = workArea;
            so.FindProperty("martInsideZone").objectReferenceValue = martIn;
            so.FindProperty("martOutsideZone").objectReferenceValue = martOut;
            so.FindProperty("smokingZone").objectReferenceValue = smokingArea;
            so.FindProperty("homeZone").objectReferenceValue = home;
            so.FindProperty("martExitGate").objectReferenceValue = martGate;
            so.FindProperty("cigarettePackTemplate").objectReferenceValue = packT != null ? packT.gameObject : null;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log("[Day1StoryBuilder] Built 'Story' (Day1StoryController, Mike, 6 routes, 6 zones).");
        }

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

        private static StoryZone Zone(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var t = Child(parent, name, center, 0f);
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
