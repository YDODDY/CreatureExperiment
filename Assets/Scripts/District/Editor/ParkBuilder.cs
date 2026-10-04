using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CreatureExperiment.DailyLife;

namespace CreatureExperiment.DistrictEditor
{
    /// <summary>
    /// Playground + Pocket Garden pass. Builds the scene root "Parks" (replaced on every run, separate from the "District"
    /// root so a District rebuild never removes it):
    /// - PlaygroundPark on the District's big Park block (ex "Playground" placeholder, x 63.2..101, z 15.3..39.9): grass
    ///   lawn, a gravel path Main Street → north Park plus a west → east crossing through the playground, a rubber
    ///   playground with a curb (gaps on the paths), a rideable <see cref="SeesawRide"/>, a 2-seat swing set
    ///   (<see cref="SwingRide"/>), a jungle gym of separate bars / platform / ladder / slide colliders, long benches
    ///   (one <see cref="SittableChair"/> per seat), trees, bushes, grass clumps, bins and park lamps (<see cref="StreetLamp"/>).
    /// - PocketGarden on the Small Open Lot (x -28.6..-1.4, z 17..36.5): a quiet garden - a curving gravel walk from Main
    ///   Street to the back lane with two side nooks, benches, trees, a street hedge, planters / flower beds, low lamps.
    /// Moving parts (seesaw beam, swing pivots) carry NavMeshModifier ignoreFromBuild + a kinematic Rigidbody; bushes,
    /// grass and flowers have no colliders (only trunks, hedges, planters, equipment and benches block).
    /// The District placeholders this replaces are removed (and no longer built by DistrictBlockoutBuilder).
    /// Afterwards: rebake the NavMesh.
    /// </summary>
    public static class ParkBuilder
    {
        private const float G = DistrictBlockoutBuilder.Ground;
        private const string RootName = "Parks";
        private const string MatFolder = "Assets/Materials/District";

        public static readonly Rect BigPark = Rect.MinMaxRect(63.2f, 15.3f, 101f, 39.9f);
        public static readonly Rect PlayArea = Rect.MinMaxRect(78f, 18f, 99f, 37.5f);
        public static readonly Rect Garden = Rect.MinMaxRect(-28.6f, 17f, -1.4f, 36.5f);

        private const float GrassTop = 0.02f, PathTop = 0.03f, RubberTop = 0.04f;

        private static readonly Dictionary<string, Material> s_mats = new Dictionary<string, Material>();
        private static readonly List<Vector2[]> s_paths = new List<Vector2[]>();
        private static readonly List<float> s_pathWidths = new List<float>();
        private static readonly List<Vector3> s_blockers = new List<Vector3>(); // x, z, radius

        [MenuItem("Tools/CreatureExperiment/Parks/Build Playground Park + Pocket Garden")]
        public static void Build()
        {
            s_mats.Clear();
            GameObject treeT = FindTreeTemplate();
            GameObject binT = GameObject.Find("District/StreetFurniture/TrashBins/Bin_Park");
            if (treeT == null || binT == null)
                Debug.LogWarning("[Parks] tree / bin template missing - those props are skipped.");

            RemoveLegacy();
            var old = GameObject.Find(RootName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Parks");

            BuildPlaygroundPark(root.transform, treeT, binT);
            BuildPocketGarden(root.transform, treeT, binT);

            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[Parks] Playground Park + Pocket Garden built. Rebake the NavMesh.");
        }

        // =============================================================================================================
        // Playground Park
        // =============================================================================================================

        private static void BuildPlaygroundPark(Transform parent, GameObject treeT, GameObject binT)
        {
            ResetLayout();
            var park = Node(parent, "PlaygroundPark", Vector3.zero);

            // --- Ground: lawn, paths, playground surface
            var ground = Node(park, "Ground", Vector3.zero);
            Flat(ground, "Lawn", BigPark, GrassTop, "Grass");
            Flat(ground, "Playground_Surface", PlayArea, RubberTop, "Rubber");
            var mainPath = new[]
            {
                new Vector2(72.5f, 15.3f), new Vector2(73.3f, 21f), new Vector2(74.6f, 26.5f), new Vector2(75.2f, 28.3f),
                new Vector2(76.0f, 33.5f), new Vector2(76.4f, 37.0f), new Vector2(76.9f, 39.9f),
            };
            PathLine(ground, "Path_MainStreet_to_NorthPark", mainPath, 2.6f, "Path");
            PathLine(ground, "Path_West_Entrance", new[] { new Vector2(63.2f, 29f), new Vector2(69f, 28.6f), new Vector2(75.2f, 28.3f), new Vector2(78.2f, 28.2f) }, 2.4f, "Path");
            PathLine(ground, "Path_East_Exit", new[] { new Vector2(98.8f, 28.2f), new Vector2(101f, 28.2f) }, 2.4f, "Path");
            Label(park, "PLAYGROUND", new Vector3(88.5f, G + RubberTop + 0.01f, 28.2f), 0.5f);

            // --- Curb around the playground (gaps: west crossing, east crossing, south by the bench)
            var curb = Node(park, "Playground_Curb", Vector3.zero);
            CurbX(curb, "Curb_S_W", PlayArea.xMin, 86.5f, PlayArea.yMin);
            CurbX(curb, "Curb_S_E", 90.5f, PlayArea.xMax, PlayArea.yMin);
            CurbX(curb, "Curb_N", PlayArea.xMin, PlayArea.xMax, PlayArea.yMax);
            CurbZ(curb, "Curb_W_S", PlayArea.yMin, 26.6f, PlayArea.xMin);
            CurbZ(curb, "Curb_W_N", 29.8f, PlayArea.yMax, PlayArea.xMin);
            CurbZ(curb, "Curb_E_S", PlayArea.yMin, 26.6f, PlayArea.xMax);
            CurbZ(curb, "Curb_E_N", 29.8f, PlayArea.yMax, PlayArea.xMax);

            // --- Equipment
            var equipment = Node(park, "Equipment", Vector3.zero);
            BuildSeesaw(equipment, new Vector3(81.5f, G + RubberTop, 22.5f), 0f);
            BuildSwingSet(equipment, new Vector3(85.5f, G + RubberTop, 34.5f), 180f);
            BuildJungleGym(equipment, new Vector3(93.5f, G + RubberTop, 23.6f), 0f);
            Sandbox(equipment, new Vector3(93.6f, G + RubberTop, 31.6f), 3.2f);
            Block(81.5f, 22.5f, 2.6f); Block(85.5f, 34.5f, 3.2f); Block(93.6f, 22.4f, 3f);

            // --- Benches (facing the playground / path)
            var benches = Node(park, "Benches", Vector3.zero);
            LongBench(benches, "Bench_West_1", new Vector3(71.6f, G + GrassTop, 23.8f), 90f, 2.6f, 2);
            LongBench(benches, "Bench_West_2", new Vector3(72.4f, G + GrassTop, 33.6f), 90f, 2.6f, 2);
            LongBench(benches, "Bench_Playground_North", new Vector3(93.0f, G + RubberTop, 36.6f), 180f, 2.7f, 3);
            LongBench(benches, "Bench_South", new Vector3(88.5f, G + GrassTop, 16.3f), 0f, 2.6f, 2);
            Block(71.6f, 23.8f, 1.6f); Block(72.4f, 33.6f, 1.6f); Block(88.5f, 16.3f, 1.6f);

            // --- Trees (trunks block; they and the jungle gym break the long sightlines)
            var trees = Node(park, "Trees", Vector3.zero);
            int t = 0;
            foreach (var p in new[]
            {
                new Vector3(66.0f, 18.5f, 1.1f), new Vector3(68.6f, 23.6f, 0.95f), new Vector3(66.2f, 33.4f, 1.2f), new Vector3(69.6f, 37.6f, 1.0f),
                new Vector3(100.1f, 20.5f, 1.05f), new Vector3(100.2f, 35.2f, 1.15f), new Vector3(82.6f, 38.8f, 0.9f), new Vector3(96.4f, 38.7f, 1.0f),
            })
            {
                Clone(treeT, trees, "Tree_" + (++t).ToString("00"), new Vector3(p.x, G, p.y), t * 47f, p.z);
                Block(p.x, p.y, 1.3f);
            }

            // --- Bushes (no colliders) + grass clumps
            var green = Node(park, "Greenery", Vector3.zero);
            int b = 0;
            foreach (var p in new[]
            {
                new Vector2(64.3f, 24.0f), new Vector2(64.3f, 35.8f), new Vector2(76.6f, 21.5f), new Vector2(100.2f, 24.5f),
                new Vector2(100.2f, 31.6f), new Vector2(81.5f, 16.5f), new Vector2(94.5f, 16.6f), new Vector2(69.0f, 16.4f),
            })
            {
                Bush(green, "Bush_" + (++b).ToString("00"), p, 0.8f + 0.1f * (b % 3), b);
                Block(p.x, p.y, 1.1f);
            }

            // --- Bins + lamps
            var furniture = Node(park, "Furniture", Vector3.zero);
            Clone(binT, furniture, "Bin_MainEntrance", new Vector3(75.0f, G, 17.0f), 270f, 1f);
            Clone(binT, furniture, "Bin_Playground", new Vector3(90.8f, G + RubberTop, 36.6f), 180f, 1f);
            Block(75.0f, 17.0f, 0.8f); Block(90.8f, 36.6f, 0.8f);
            var lamps = Node(park, "Lamps", Vector3.zero);
            foreach (var (n, p) in new[]
            {
                ("ParkLamp_MainEntrance", new Vector2(70.6f, 16.6f)), ("ParkLamp_WestJunction", new Vector2(70.8f, 31.0f)),
                ("ParkLamp_NorthEntrance", new Vector2(74.6f, 38.4f)), ("ParkLamp_EastEntrance", new Vector2(99.6f, 30.8f)),
                ("ParkLamp_Playground_SE", new Vector2(98.4f, 18.6f)), ("ParkLamp_Playground_NW", new Vector2(80.5f, 36.9f)),
            })
            {
                ParkLamp(lamps, n, p, 3.4f, 11f, 4f);
                Block(p.x, p.y, 0.8f);
            }

            ScatterGrass(green, BigPark, 70, 1234, PlayArea);
        }

        // ---- Seesaw ----------------------------------------------------------------------------------------------

        private static void BuildSeesaw(Transform parent, Vector3 pos, float yaw)
        {
            var root = Node(parent, "Seesaw", pos, yaw);
            var ride = root.gameObject.AddComponent<SeesawRide>();
            const float pivotY = 0.92f, half = 2.1f, seatX = 1.85f;

            var stand = Node(root, "Base", Vector3.zero);
            Prim(PrimitiveType.Cube, stand, "Foot", new Vector3(0f, 0.04f, 0f), new Vector3(0.7f, 0.08f, 0.8f), "Metal", true);
            Prim(PrimitiveType.Cube, stand, "Post_N", new Vector3(0f, pivotY * 0.5f, 0.25f), new Vector3(0.26f, pivotY, 0.08f), "PlayBlue", true);
            Prim(PrimitiveType.Cube, stand, "Post_S", new Vector3(0f, pivotY * 0.5f, -0.25f), new Vector3(0.26f, pivotY, 0.08f), "PlayBlue", true);
            Prim(PrimitiveType.Cylinder, stand, "Axle", new Vector3(0f, pivotY, 0f), new Vector3(0.1f, 0.3f, 0.1f), "Metal", false, Quaternion.Euler(90f, 0f, 0f));
            foreach (float x in new[] { seatX, -seatX })
                Prim(PrimitiveType.Cylinder, stand, x > 0 ? "Bumper_A" : "Bumper_B", new Vector3(x, 0.085f, 0f), new Vector3(0.32f, 0.085f, 0.32f), "Tire", true);

            var pivot = Node(root, "Pivot", new Vector3(0f, pivotY, 0f));
            MovingPart(pivot);
            var beam = Node(pivot, "Beam", Vector3.zero);
            Prim(PrimitiveType.Cube, beam, "Plank", Vector3.zero, new Vector3(half * 2f, 0.08f, 0.26f), "PlayYellow", true);
            Transform seatA = null, seatB = null;
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? seatX : -seatX;
                string tag = s == 0 ? "A" : "B";
                var seat = Prim(PrimitiveType.Cube, beam, "Seat" + tag, new Vector3(x, 0.08f, 0f), new Vector3(0.5f, 0.08f, 0.38f), "PlayRed", true);
                var seatComp = seat.AddComponent<SeesawSeat>();
                var so = new SerializedObject(seatComp);
                so.FindProperty("ride").objectReferenceValue = ride;
                so.FindProperty("side").intValue = s;
                so.ApplyModifiedPropertiesWithoutUndo();
                var point = Node(beam, "SitPoint" + tag, new Vector3(x, 0.12f, 0f));
                if (s == 0) seatA = point; else seatB = point;
                float hx = x - Mathf.Sign(x) * 0.36f; // handle in front of the seat (toward the pivot)
                Prim(PrimitiveType.Cylinder, beam, "HandlePost_" + tag, new Vector3(hx, 0.27f, 0f), new Vector3(0.05f, 0.23f, 0.05f), "Metal", false);
                Prim(PrimitiveType.Cylinder, beam, "HandleBar_" + tag, new Vector3(hx, 0.5f, 0f), new Vector3(0.045f, 0.2f, 0.045f), "Metal", false, Quaternion.Euler(90f, 0f, 0f));
            }
            var exitA = Node(root, "ExitPointA", new Vector3(seatX, 0f, 1.15f), 0f);
            var exitB = Node(root, "ExitPointB", new Vector3(-seatX, 0f, 1.15f), 0f);

            var rs = new SerializedObject(ride);
            rs.FindProperty("pivot").objectReferenceValue = pivot;
            rs.FindProperty("seatPointA").objectReferenceValue = seatA;
            rs.FindProperty("seatPointB").objectReferenceValue = seatB;
            rs.FindProperty("exitPointA").objectReferenceValue = exitA;
            rs.FindProperty("exitPointB").objectReferenceValue = exitB;
            rs.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- Swing set -------------------------------------------------------------------------------------------

        private static void BuildSwingSet(Transform parent, Vector3 pos, float yaw)
        {
            var root = Node(parent, "SwingSet", pos, yaw);
            const float barY = 2.65f, legX = 2.6f, legSpread = 1.2f, pivotY = 2.58f, rope = 2.05f;
            var frame = Node(root, "Frame", Vector3.zero);
            Rod(frame, "TopBar", new Vector3(-legX - 0.1f, barY, 0f), new Vector3(legX + 0.1f, barY, 0f), 0.06f, "PlayBlue", true);
            foreach (float x in new[] { -legX, legX })
                foreach (float z in new[] { -legSpread, legSpread })
                    Rod(frame, $"Leg_{(x < 0 ? "L" : "R")}{(z < 0 ? "B" : "F")}", new Vector3(x, barY, 0f), new Vector3(x, 0f, z), 0.055f, "PlayBlue", true);
            foreach (float x in new[] { -legX, legX })
                Rod(frame, "Brace_" + (x < 0 ? "L" : "R"), new Vector3(x, 0.9f, -legSpread * 0.66f), new Vector3(x, 0.9f, legSpread * 0.66f), 0.035f, "Metal", false);

            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.85f : 0.85f;
                string tag = i == 0 ? "L" : "R";
                var unit = Node(root, "Swing_" + tag, new Vector3(x, pivotY, 0f));
                var ride = unit.gameObject.AddComponent<SwingRide>();
                var pivot = Node(unit, "Pivot", Vector3.zero);
                MovingPart(pivot);
                Prim(PrimitiveType.Cylinder, pivot, "Hanger", new Vector3(0f, 0.03f, 0f), new Vector3(0.08f, 0.32f, 0.08f), "Metal", false, Quaternion.Euler(0f, 0f, 90f));
                foreach (float rx in new[] { -0.24f, 0.24f })
                    Rod(pivot, rx < 0 ? "Rope_L" : "Rope_R", new Vector3(rx, 0f, 0f), new Vector3(rx, -rope, 0f), 0.014f, "Chain", false);
                Prim(PrimitiveType.Cube, pivot, "Seat", new Vector3(0f, -rope - 0.03f, 0f), new Vector3(0.56f, 0.05f, 0.26f), i == 0 ? "PlayRed" : "PlayYellow", true);
                var seatPoint = Node(pivot, "SitPoint", new Vector3(0f, -rope, 0f));
                var exit = Node(root, "ExitPoint_" + tag, new Vector3(i == 0 ? -legX - 0.65f : legX + 0.65f, 0f, 0f), 0f);

                var so = new SerializedObject(ride);
                so.FindProperty("pivot").objectReferenceValue = pivot;
                so.FindProperty("seatPoint").objectReferenceValue = seatPoint;
                so.FindProperty("exitPoint").objectReferenceValue = exit;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ---- Jungle gym --------------------------------------------------------------------------------------------

        private static void BuildJungleGym(Transform parent, Vector3 pos, float yaw)
        {
            var root = Node(parent, "JungleGym", pos, yaw);
            const float top = 2.2f, deck = 1.2f, zH = 1.25f;
            var posts = Node(root, "Posts", Vector3.zero);
            foreach (float x in new[] { -2f, 0f, 2f })
                foreach (float z in new[] { -zH, zH })
                    Rod(posts, $"Post_{x:0}_{(z < 0 ? "S" : "N")}", new Vector3(x, 0f, z), new Vector3(x, top + 0.05f, z), 0.055f, "PlayRed", true);

            var deckT = Node(root, "Platform", Vector3.zero);
            Prim(PrimitiveType.Cube, deckT, "Deck", new Vector3(1f, deck, 0f), new Vector3(2f, 0.08f, zH * 2f), "PlayYellow", true);
            // Guard rails around the deck: gaps for the ladder (east) and the slide (south).
            Rod(deckT, "Guard_N", new Vector3(0f, 1.75f, zH), new Vector3(2f, 1.75f, zH), 0.03f, "Metal", true);
            Rod(deckT, "Guard_S_W", new Vector3(0f, 1.75f, -zH), new Vector3(0.6f, 1.75f, -zH), 0.03f, "Metal", true);
            Rod(deckT, "Guard_S_E", new Vector3(1.4f, 1.75f, -zH), new Vector3(2f, 1.75f, -zH), 0.03f, "Metal", true);
            Rod(deckT, "Guard_E_S", new Vector3(2f, 1.75f, -zH), new Vector3(2f, 1.75f, -0.5f), 0.03f, "Metal", true);
            Rod(deckT, "Guard_E_N", new Vector3(2f, 1.75f, 0.5f), new Vector3(2f, 1.75f, zH), 0.03f, "Metal", true);

            var bars = Node(root, "MonkeyBars", Vector3.zero);
            Rod(bars, "Rail_S", new Vector3(-2f, top, -zH), new Vector3(2f, top, -zH), 0.04f, "PlayBlue", true);
            Rod(bars, "Rail_N", new Vector3(-2f, top, zH), new Vector3(2f, top, zH), 0.04f, "PlayBlue", true);
            for (int k = 0; k < 5; k++)
            {
                float x = -1.8f + 0.4f * k;
                Rod(bars, "Rung_" + k, new Vector3(x, top, -zH), new Vector3(x, top, zH), 0.03f, "Metal", true);
            }
            Rod(bars, "Rail_W_Low", new Vector3(-2f, 1.0f, -zH), new Vector3(-2f, 1.0f, zH), 0.035f, "PlayBlue", true);

            var ladder = Node(root, "Ladder", Vector3.zero);
            foreach (float z in new[] { -0.42f, 0.42f })
                Rod(ladder, z < 0 ? "Side_S" : "Side_N", new Vector3(2.35f, 0f, z), new Vector3(2.05f, deck + 0.45f, z), 0.035f, "PlayRed", true);
            for (int k = 1; k <= 4; k++)
            {
                float y = 0.3f * k;
                float x = Mathf.Lerp(2.35f, 2.05f, y / (deck + 0.45f));
                Rod(ladder, "Step_" + k, new Vector3(x, y, -0.42f), new Vector3(x, y, 0.42f), 0.025f, "Metal", true);
            }

            // Slide off the south edge of the deck.
            var slide = Node(root, "Slide", Vector3.zero);
            Vector3 a = new Vector3(1f, deck + 0.02f, -zH), bEnd = new Vector3(1f, 0.15f, -zH - 2.45f);
            Vector3 mid = (a + bEnd) * 0.5f;
            float len = Vector3.Distance(a, bEnd) + 0.1f;
            Quaternion rot = Quaternion.LookRotation((a - bEnd).normalized, Vector3.up);
            Prim(PrimitiveType.Cube, slide, "Bed", mid, new Vector3(0.6f, 0.05f, len), "PlayBlue", true, rot);
            Prim(PrimitiveType.Cube, slide, "Side_W", mid + rot * new Vector3(-0.32f, 0.1f, 0f), new Vector3(0.05f, 0.22f, len), "PlayBlue", true, rot);
            Prim(PrimitiveType.Cube, slide, "Side_E", mid + rot * new Vector3(0.32f, 0.1f, 0f), new Vector3(0.05f, 0.22f, len), "PlayBlue", true, rot);
        }

        // A low wooden frame (blocks / can be stepped over) around a sand bed.
        private static void Sandbox(Transform parent, Vector3 pos, float size)
        {
            var box = Node(parent, "Sandbox", pos);
            float h = size * 0.5f;
            Prim(PrimitiveType.Cube, box, "Sand", new Vector3(0f, 0.1f, 0f), new Vector3(size - 0.2f, 0.06f, size - 0.2f), "Sand", false, null, false);
            Prim(PrimitiveType.Cube, box, "Rim_N", new Vector3(0f, 0.12f, h - 0.08f), new Vector3(size, 0.24f, 0.16f), "Wood", true);
            Prim(PrimitiveType.Cube, box, "Rim_S", new Vector3(0f, 0.12f, -h + 0.08f), new Vector3(size, 0.24f, 0.16f), "Wood", true);
            Prim(PrimitiveType.Cube, box, "Rim_E", new Vector3(h - 0.08f, 0.12f, 0f), new Vector3(0.16f, 0.24f, size - 0.32f), "Wood", true);
            Prim(PrimitiveType.Cube, box, "Rim_W", new Vector3(-h + 0.08f, 0.12f, 0f), new Vector3(0.16f, 0.24f, size - 0.32f), "Wood", true);
            Prim(PrimitiveType.Cube, box, "Bucket", new Vector3(0.6f, 0.2f, 0.4f), new Vector3(0.2f, 0.18f, 0.2f), "PlayRed", false);
            Prim(PrimitiveType.Cube, box, "Shovel", new Vector3(-0.5f, 0.15f, -0.3f), new Vector3(0.08f, 0.03f, 0.4f), "PlayYellow", false, Quaternion.Euler(0f, 35f, 0f));
        }

        // =============================================================================================================
        // Pocket Garden
        // =============================================================================================================

        private static void BuildPocketGarden(Transform parent, GameObject treeT, GameObject binT)
        {
            ResetLayout();
            var garden = Node(parent, "PocketGarden", Vector3.zero);
            var ground = Node(garden, "Ground", Vector3.zero);
            Flat(ground, "Lawn", Garden, GrassTop, "Grass");
            PathLine(ground, "Walk_Main", new[]
            {
                new Vector2(-9.0f, 17.0f), new Vector2(-9.8f, 20.5f), new Vector2(-12.5f, 24.0f), new Vector2(-16.8f, 26.6f),
                new Vector2(-19.6f, 29.6f), new Vector2(-19.8f, 33.2f), new Vector2(-17.2f, 36.9f),
            }, 2.0f, "Path");
            PathLine(ground, "Walk_EastNook", new[] { new Vector2(-12.5f, 24.0f), new Vector2(-8.0f, 27.2f), new Vector2(-5.0f, 31.0f) }, 1.6f, "Path");
            PathLine(ground, "Walk_WestNook", new[] { new Vector2(-19.6f, 29.6f), new Vector2(-23.8f, 30.8f) }, 1.5f, "Path");
            Disc(ground, "Nook_East", new Vector2(-4.8f, 32.6f), 1.8f, "Path");
            Disc(ground, "Nook_West", new Vector2(-24.5f, 31.0f), 1.6f, "Path");
            Block(-4.8f, 32.6f, 1.8f); Block(-24.5f, 31.0f, 1.6f);

            // Street hedge (blocks; a gap at the entrance walk) - turns the lot away from Main Street.
            var hedge = Node(garden, "Hedge_Street", Vector3.zero);
            Prim(PrimitiveType.Cube, hedge, "Hedge_W", new Vector3((-28.2f + -11.0f) * 0.5f, G + 0.38f, 17.6f), new Vector3(17.2f, 0.75f, 0.6f), "Hedge", true);
            Prim(PrimitiveType.Cube, hedge, "Hedge_E", new Vector3((-7.0f + -1.8f) * 0.5f, G + 0.38f, 17.6f), new Vector3(5.2f, 0.75f, 0.6f), "Hedge", true);

            var benches = Node(garden, "Benches", Vector3.zero);
            LongBench(benches, "Bench_EastNook", new Vector3(-3.4f, G + PathTop, 33.6f), 225f, 2.2f, 2);
            LongBench(benches, "Bench_WestNook", new Vector3(-26.0f, G + PathTop, 31.4f), 100f, 2.2f, 2);
            LongBench(benches, "Bench_Walk", new Vector3(-13.9f, G + GrassTop, 21.6f), 55f, 2.2f, 2);
            Block(-13.9f, 21.6f, 1.5f);

            var beds = Node(garden, "Planters", Vector3.zero);
            Planter(beds, "Planter_East", new Vector2(-4.6f, 25.5f), new Vector2(2.8f, 1.2f), 20f, 11);
            Planter(beds, "Planter_WestNook", new Vector2(-22.8f, 33.8f), new Vector2(1.6f, 0.9f), 0f, 12);
            RoundBed(beds, "FlowerBed_Round", new Vector2(-16.0f, 30.8f), 1.3f, 13);
            Block(-4.6f, 25.5f, 1.8f); Block(-22.8f, 33.8f, 1.1f); Block(-16.0f, 30.8f, 1.6f);

            var trees = Node(garden, "Trees", Vector3.zero);
            int t = 0;
            foreach (var p in new[] { new Vector3(-24.8f, 20.8f, 1.15f), new Vector3(-25.8f, 35.2f, 0.95f), new Vector3(-5.2f, 22.0f, 1.0f), new Vector3(-13.4f, 32.8f, 1.1f) })
            {
                Clone(treeT, trees, "Tree_" + (++t).ToString("00"), new Vector3(p.x, G, p.y), t * 61f, p.z);
                Block(p.x, p.y, 1.3f);
            }

            var green = Node(garden, "Greenery", Vector3.zero);
            int b = 0;
            foreach (var p in new[]
            {
                new Vector2(-27.6f, 24.5f), new Vector2(-27.4f, 27.8f), new Vector2(-11.6f, 35.6f), new Vector2(-2.4f, 28.6f),
                new Vector2(-21.5f, 19.4f), new Vector2(-2.6f, 19.8f), new Vector2(-7.4f, 35.4f),
            })
            {
                Bush(green, "Bush_" + (++b).ToString("00"), p, 0.7f + 0.1f * (b % 3), 100 + b);
                Block(p.x, p.y, 1.0f);
            }

            var furniture = Node(garden, "Furniture", Vector3.zero);
            Clone(binT, furniture, "Bin_Entrance", new Vector3(-7.6f, G, 18.6f), 270f, 1f);
            Block(-7.6f, 18.6f, 0.8f);
            var lamps = Node(garden, "Lamps", Vector3.zero);
            foreach (var (n, p) in new[]
            {
                ("GardenLamp_Entrance", new Vector2(-11.4f, 18.9f)), ("GardenLamp_Walk", new Vector2(-14.4f, 27.6f)),
                ("GardenLamp_WestNook", new Vector2(-22.6f, 28.6f)), ("GardenLamp_EastNook", new Vector2(-6.6f, 34.8f)),
            })
            {
                ParkLamp(lamps, n, p, 2.7f, 7f, 1.8f);
                Block(p.x, p.y, 0.7f);
            }

            ScatterGrass(green, Rect.MinMaxRect(Garden.xMin, 18.2f, Garden.xMax, Garden.yMax), 45, 4321, Rect.zero);
        }

        // =============================================================================================================
        // Props
        // =============================================================================================================

        /// <summary>A long bench (legs, back, one plank per seat) - each plank section is its own <see cref="SittableChair"/>.</summary>
        private static void LongBench(Transform parent, string name, Vector3 pos, float yaw, float length, int seats)
        {
            var bench = Node(parent, name, pos, yaw);
            float half = length * 0.5f;
            Prim(PrimitiveType.Cube, bench, "Back", new Vector3(0f, 0.78f, -0.21f), new Vector3(length, 0.36f, 0.06f), "Wood", true);
            for (int i = 0; i <= seats; i++)
            {
                float x = Mathf.Lerp(-half + 0.12f, half - 0.12f, i / (float)seats);
                Prim(PrimitiveType.Cube, bench, "Leg_" + i, new Vector3(x, 0.21f, -0.02f), new Vector3(0.07f, 0.42f, 0.42f), "Metal", true);
                Prim(PrimitiveType.Cube, bench, "BackPost_" + i, new Vector3(x, 0.66f, -0.22f), new Vector3(0.06f, 0.5f, 0.05f), "Metal", false);
            }
            float w = length / seats;
            for (int i = 0; i < seats; i++)
            {
                float cx = -half + w * (i + 0.5f);
                var seat = Node(bench, "Seat_" + (i + 1), new Vector3(cx, 0f, 0f));
                Prim(PrimitiveType.Cube, seat, "Plank", new Vector3(0f, 0.45f, 0f), new Vector3(w - 0.02f, 0.07f, 0.45f), "Wood", true);
                var sit = Node(seat, "SitPoint", new Vector3(0f, 1.15f, 0.05f));
                var standPt = Node(seat, "StandPoint", new Vector3(0f, 0.1f, 0.85f));
                var chair = seat.gameObject.AddComponent<SittableChair>();
                var so = new SerializedObject(chair);
                so.FindProperty("sitPoint").objectReferenceValue = sit;
                so.FindProperty("standPoint").objectReferenceValue = standPt;
                so.FindProperty("sitPitch").floatValue = 8f;
                so.FindProperty("sitPrompt").stringValue = "E · 벤치에 앉기";
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void ParkLamp(Transform parent, string name, Vector2 p, float height, float range, float intensity)
        {
            var lamp = Node(parent, name, new Vector3(p.x, G, p.y));
            Prim(PrimitiveType.Cylinder, lamp, "Base", new Vector3(0f, 0.15f, 0f), new Vector3(0.24f, 0.15f, 0.24f), "LampPaint", true);
            Prim(PrimitiveType.Cylinder, lamp, "Pole", new Vector3(0f, height * 0.5f, 0f), new Vector3(0.1f, height * 0.5f, 0.1f), "LampPaint", true);
            var head = Node(lamp, "Lamp", new Vector3(0f, height, 0f));
            Prim(PrimitiveType.Cylinder, head, "Cap", new Vector3(0f, 0.36f, 0f), new Vector3(0.4f, 0.03f, 0.4f), "LampPaint", false);
            Prim(PrimitiveType.Sphere, head, "LampLens", new Vector3(0f, 0.17f, 0f), new Vector3(0.34f, 0.34f, 0.34f), "Lens", false, null, false);
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(head, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = new Color(1f, 0.88f, 0.7f);
            light.shadows = LightShadows.None;
            var streetLamp = head.gameObject.AddComponent<StreetLamp>();
            var so = new SerializedObject(streetLamp);
            so.FindProperty("lampLight").objectReferenceValue = light;
            so.FindProperty("lens").objectReferenceValue = head.Find("LampLens").GetComponent<Renderer>();
            so.FindProperty("offMaterial").objectReferenceValue = Mat("LightOff");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Bush(Transform parent, string name, Vector2 p, float size, int seed)
        {
            var rng = new System.Random(seed);
            var bush = Node(parent, name, new Vector3(p.x, G, p.y), (float)rng.NextDouble() * 360f);
            for (int i = 0; i < 3; i++)
            {
                float s = size * (0.7f + 0.3f * (float)rng.NextDouble());
                var offset = new Vector3((i - 1) * size * 0.45f, s * 0.32f, ((float)rng.NextDouble() - 0.5f) * size * 0.5f);
                Prim(PrimitiveType.Sphere, bush, "Lobe_" + i, offset, new Vector3(s, s * 0.75f, s), i == 1 ? "BushDark" : "Bush", false);
            }
        }

        private static void Planter(Transform parent, string name, Vector2 p, Vector2 size, float yaw, int seed)
        {
            var bed = Node(parent, name, new Vector3(p.x, G, p.y), yaw);
            Prim(PrimitiveType.Cube, bed, "Rim", new Vector3(0f, 0.22f, 0f), new Vector3(size.x, 0.44f, size.y), "Planter", true);
            Prim(PrimitiveType.Cube, bed, "Soil", new Vector3(0f, 0.445f, 0f), new Vector3(size.x - 0.16f, 0.02f, size.y - 0.16f), "Soil", false);
            Flowers(bed, new Vector2(size.x - 0.3f, size.y - 0.3f), 0.46f, seed, false);
        }

        private static void RoundBed(Transform parent, string name, Vector2 p, float radius, int seed)
        {
            var bed = Node(parent, name, new Vector3(p.x, G, p.y));
            Prim(PrimitiveType.Cylinder, bed, "Rim", new Vector3(0f, 0.18f, 0f), new Vector3(radius * 2f, 0.18f, radius * 2f), "Planter", true);
            Prim(PrimitiveType.Cylinder, bed, "Soil", new Vector3(0f, 0.365f, 0f), new Vector3(radius * 2f - 0.16f, 0.01f, radius * 2f - 0.16f), "Soil", false);
            Flowers(bed, new Vector2(radius * 1.4f, radius * 1.4f), 0.38f, seed, true);
        }

        private static void Flowers(Transform bed, Vector2 area, float y, int seed, bool round)
        {
            var rng = new System.Random(seed);
            string[] colours = { "FlowerRed", "FlowerYellow", "FlowerPurple", "FlowerWhite" };
            int n = Mathf.RoundToInt(area.x * area.y * 9f);
            for (int i = 0; i < n; i++)
            {
                float x = ((float)rng.NextDouble() - 0.5f) * area.x, z = ((float)rng.NextDouble() - 0.5f) * area.y;
                if (round && new Vector2(x / area.x, z / area.y).magnitude > 0.5f)
                    continue;
                float h = 0.12f + 0.12f * (float)rng.NextDouble();
                Prim(PrimitiveType.Cube, bed, "Stem", new Vector3(x, y + h * 0.5f, z), new Vector3(0.02f, h, 0.02f), "Bush", false, null, false);
                Prim(PrimitiveType.Sphere, bed, "Bloom", new Vector3(x, y + h, z), Vector3.one * 0.09f, colours[rng.Next(colours.Length)], false, null, false);
            }
        }

        /// <summary>Low grass clumps (no colliders, no shadows) on the open lawn, kept off paths and props.</summary>
        private static void ScatterGrass(Transform parent, Rect area, int count, int seed, Rect exclude)
        {
            var rng = new System.Random(seed);
            var g = Node(parent, "GrassClumps", Vector3.zero);
            int placed = 0;
            for (int tries = 0; tries < count * 20 && placed < count; tries++)
            {
                var p = new Vector2(Mathf.Lerp(area.xMin + 0.5f, area.xMax - 0.5f, (float)rng.NextDouble()),
                                    Mathf.Lerp(area.yMin + 0.5f, area.yMax - 0.5f, (float)rng.NextDouble()));
                if (exclude.width > 0f && exclude.Contains(p))
                    continue;
                if (!Clear(p, 0.5f))
                    continue;
                var clump = Node(g, "Clump", new Vector3(p.x, G + GrassTop, p.y), (float)rng.NextDouble() * 360f);
                float s = 0.3f + 0.25f * (float)rng.NextDouble();
                Prim(PrimitiveType.Sphere, clump, "A", new Vector3(0f, 0.03f, 0f), new Vector3(s, s * 0.35f, s * 0.8f), "GrassClump", false, null, false);
                Prim(PrimitiveType.Sphere, clump, "B", new Vector3(s * 0.35f, 0.02f, s * 0.2f), new Vector3(s * 0.7f, s * 0.28f, s * 0.6f), "GrassClumpDark", false, null, false);
                s_blockers.Add(new Vector3(p.x, p.y, 0.4f));
                placed++;
            }
        }

        // =============================================================================================================
        // Layout bookkeeping (keeps scattered grass off paths and props)
        // =============================================================================================================

        private static void ResetLayout()
        {
            s_paths.Clear();
            s_pathWidths.Clear();
            s_blockers.Clear();
        }

        private static void Block(float x, float z, float r) => s_blockers.Add(new Vector3(x, z, r));

        private static bool Clear(Vector2 p, float margin)
        {
            foreach (var b in s_blockers)
                if (Vector2.Distance(p, new Vector2(b.x, b.y)) < b.z + margin)
                    return false;
            for (int i = 0; i < s_paths.Count; i++)
            {
                var pts = s_paths[i];
                for (int k = 0; k + 1 < pts.Length; k++)
                    if (DistanceToSegment(p, pts[k], pts[k + 1]) < s_pathWidths[i] * 0.5f + margin)
                        return false;
            }
            return true;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
            return Vector2.Distance(p, a + ab * t);
        }

        // =============================================================================================================
        // Legacy placeholders (District v0.2) replaced by this pass
        // =============================================================================================================

        private static void RemoveLegacy()
        {
            foreach (string path in new[]
            {
                "District/Leisure/Playground_Ground", "District/Leisure/Playground_PlayArea", "District/Leisure/Label_PLAYGROUND",
                "District/Leisure/Playground_Slide_Placeholder", "District/Leisure/Playground_Swing_Placeholder",
                "District/Leisure/Playground_Climber_Placeholder", "District/Leisure/Playground_Seesaw_Placeholder",
                "District/Leisure/SmallOpenLot_Paving", "District/Leisure/Label_SMALL OPEN LOT",
                "District/StreetFurniture/Streetlamps/Lamp_Playground", "District/StreetFurniture/Streetlamps/Lamp_Playground2",
                "District/StreetFurniture/Benches/Bench_Playground", "District/StreetFurniture/Benches/Bench_Playground2",
                "District/StreetFurniture/Benches/Bench_OpenLot",
                "District/StreetFurniture/TrashBins/Bin_OpenLot", "District/StreetFurniture/TrashBins/Bin_Playground",
            })
            {
                var go = GameObject.Find(path);
                if (go != null)
                    Undo.DestroyObjectImmediate(go);
            }
            var trees = GameObject.Find("District/StreetFurniture/Trees");
            if (trees != null)
                for (int i = trees.transform.childCount - 1; i >= 0; i--)
                {
                    var t = trees.transform.GetChild(i);
                    var p = new Vector2(t.position.x, t.position.z);
                    if (BigPark.Contains(p) || Garden.Contains(p))
                        Undo.DestroyObjectImmediate(t.gameObject);
                }
        }

        private static GameObject FindTreeTemplate()
        {
            var trees = GameObject.Find("District/StreetFurniture/Trees");
            if (trees == null)
                return null;
            foreach (Transform t in trees.transform)
            {
                var p = new Vector2(t.position.x, t.position.z);
                if (!BigPark.Contains(p) && !Garden.Contains(p))
                    return t.gameObject;
            }
            return null;
        }

        // =============================================================================================================
        // Helpers
        // =============================================================================================================

        private static Transform Node(Transform parent, string name, Vector3 localPos, float yaw = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go.transform;
        }

        /// <summary>Seesaw beam / swing pivot: moved by script, kept out of the NavMesh bake.</summary>
        private static void MovingPart(Transform t)
        {
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
            var mod = t.gameObject.AddComponent<NavMeshModifier>();
            mod.ignoreFromBuild = true;
        }

        private static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 localScale, string mat,
            bool collider, Quaternion? localRot = null, bool shadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!collider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.transform.localScale = localScale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Mat(mat);
            if (!shadows)
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A cylinder bar from <paramref name="a"/> to <paramref name="b"/> (parent-local).</summary>
        private static GameObject Rod(Transform parent, string name, Vector3 a, Vector3 b, float radius, string mat, bool collider)
        {
            Vector3 d = b - a;
            return Prim(PrimitiveType.Cylinder, parent, name, (a + b) * 0.5f, new Vector3(radius * 2f, d.magnitude * 0.5f, radius * 2f), mat, collider,
                Quaternion.FromToRotation(Vector3.up, d.normalized));
        }

        // Thin ground overlay (world rect, no collider - the District base slab is the floor).
        private static void Flat(Transform parent, string name, Rect r, float top, string mat)
        {
            const float thick = 0.02f;
            Prim(PrimitiveType.Cube, parent, name, new Vector3(r.center.x, G + top - thick * 0.5f, r.center.y), new Vector3(r.width, thick, r.height), mat, false, null, false);
        }

        private static void PathLine(Transform parent, string name, Vector2[] pts, float width, string mat)
        {
            const float thick = 0.02f;
            var line = Node(parent, name, Vector3.zero);
            float y = G + PathTop - thick * 0.5f;
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1], d = b - a;
                float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                Prim(PrimitiveType.Cube, line, "Segment_" + i, new Vector3((a.x + b.x) * 0.5f, y, (a.y + b.y) * 0.5f), new Vector3(width, thick, d.magnitude),
                    mat, false, Quaternion.Euler(0f, yaw, 0f), false);
            }
            for (int i = 1; i + 1 < pts.Length; i++) // round joints smooth the bends
                Prim(PrimitiveType.Cylinder, line, "Joint_" + i, new Vector3(pts[i].x, y, pts[i].y), new Vector3(width, thick * 0.5f, width), mat, false, null, false);
            s_paths.Add(pts);
            s_pathWidths.Add(width);
        }

        private static void Disc(Transform parent, string name, Vector2 c, float radius, string mat)
        {
            const float thick = 0.02f;
            Prim(PrimitiveType.Cylinder, parent, name, new Vector3(c.x, G + PathTop - thick * 0.5f + 0.001f, c.y), new Vector3(radius * 2f, thick * 0.5f, radius * 2f), mat, false, null, false);
        }

        private static void CurbX(Transform parent, string name, float x0, float x1, float z)
            => Prim(PrimitiveType.Cube, parent, name, new Vector3((x0 + x1) * 0.5f, G + 0.06f, z), new Vector3(x1 - x0, 0.12f, 0.16f), "Curb", true);

        private static void CurbZ(Transform parent, string name, float z0, float z1, float x)
            => Prim(PrimitiveType.Cube, parent, name, new Vector3(x, G + 0.06f, (z0 + z1) * 0.5f), new Vector3(0.16f, 0.12f, z1 - z0), "Curb", true);

        private static void Clone(GameObject template, Transform parent, string name, Vector3 pos, float yaw, float scale)
        {
            if (template == null)
                return;
            var go = (GameObject)Object.Instantiate(template);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            go.SetActive(true);
        }

        private static void Label(Transform parent, string text, Vector3 pos, float size)
        {
            var go = new GameObject("Label_" + text);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = size * 0.15f;
            tm.color = new Color(1f, 1f, 1f, 0.8f);
            go.AddComponent<WorldTextFont>();
        }

        private static Material Mat(string key)
        {
            if (s_mats.TryGetValue(key, out var m) && m != null)
                return m;
            m = LoadMat(key);
            s_mats[key] = m;
            return m;
        }

        private static Material LoadMat(string key)
        {
            string existing = key switch
            {
                "Grass" => "MAT_Grass", "Sand" => "MAT_Sand", "Metal" => "MAT_Metal_Dull", "Wood" => "MAT_Bench_Wood",
                "PlayRed" => "MAT_Play_Red", "PlayBlue" => "MAT_Play_Blue", "PlayYellow" => "MAT_Play_Yellow",
                "Lens" => "MAT_Light_StreetLens", "LightOff" => "MAT_Light_Off", _ => null,
            };
            if (existing != null)
                foreach (string guid in AssetDatabase.FindAssets(existing + " t:Material"))
                {
                    var found = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (found != null && found.name == existing)
                        return found;
                }

            Color c;
            switch (key)
            {
                case "Path": c = new Color(0.66f, 0.61f, 0.53f); break;
                case "Rubber": c = new Color(0.66f, 0.33f, 0.26f); break;
                case "Curb": c = new Color(0.72f, 0.72f, 0.7f); break;
                case "Tire": c = new Color(0.12f, 0.12f, 0.13f); break;
                case "Chain": c = new Color(0.55f, 0.56f, 0.58f); break;
                case "Hedge": c = new Color(0.17f, 0.34f, 0.15f); break;
                case "Bush": c = new Color(0.24f, 0.45f, 0.2f); break;
                case "BushDark": c = new Color(0.18f, 0.36f, 0.16f); break;
                case "GrassClump": c = new Color(0.36f, 0.56f, 0.24f); break;
                case "GrassClumpDark": c = new Color(0.27f, 0.47f, 0.19f); break;
                case "Planter": c = new Color(0.62f, 0.6f, 0.56f); break;
                case "Soil": c = new Color(0.3f, 0.21f, 0.14f); break;
                case "FlowerRed": c = new Color(0.86f, 0.18f, 0.2f); break;
                case "FlowerYellow": c = new Color(0.95f, 0.82f, 0.2f); break;
                case "FlowerPurple": c = new Color(0.55f, 0.3f, 0.75f); break;
                case "FlowerWhite": c = new Color(0.95f, 0.95f, 0.92f); break;
                case "LampPaint": c = new Color(0.16f, 0.22f, 0.2f); break;
                default: c = Color.magenta; break;
            }
            if (!AssetDatabase.IsValidFolder(MatFolder))
                AssetDatabase.CreateFolder("Assets/Materials", "District");
            string path = $"{MatFolder}/Park_{key}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", c);
            mat.color = c;
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", key == "Metal" || key == "Chain" ? 0.5f : 0.15f);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
