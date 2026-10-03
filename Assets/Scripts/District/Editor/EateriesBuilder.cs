using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CreatureExperiment.DailyLife;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DistrictEditor
{
    /// <summary>
    /// Fast Food Store + Pub / Bar pass: builds the two enterable eateries inside their District blockout blocks.
    ///
    /// - Item prefabs (Prefabs/Items/FastFood, Prefabs/Items/Pub): ready-made food (<see cref="ReadyToEatFood"/>) and
    ///   drinks (<see cref="DrinkContainer"/>; the pub's are glass - they shatter). Pub food comes on a ceramic plate
    ///   (<see cref="Breakable"/>). Built from primitives; re-running overwrites them in place (GUIDs kept).
    /// - Each shop: shell walls (named FF_* / Pub_* - <see cref="DistrictBlockoutBuilder.CarveAroundInterior"/> hollows
    ///   the block around them), a <see cref="BaristaCounter"/> order counter (the café's order → pay → pickup flow;
    ///   menu lines = <see cref="CafeMenuItem"/> on inactive prefab instances, grouped by category), tables with
    ///   <see cref="SittableChair"/> seats, lights, signs, one NPC.
    /// - The pub's bartender polishes a glass (<see cref="IdleLoopMotion"/>) and reacts to breakage
    ///   (<see cref="DisturbanceReaction"/>).
    /// Re-running replaces Street/StoreShells/FastFoodStore and Street/StoreShells/PubBar.
    /// </summary>
    public static class EateriesBuilder
    {
        private const float G = DistrictBlockoutBuilder.Ground;
        private const string MatDir = "Assets/Materials/Eateries";
        private const string FastFoodDir = "Assets/Prefabs/Items/FastFood";
        private const string PubDir = "Assets/Prefabs/Items/Pub";
        private const int TagFood = 1, TagDrink = 256;
        private const int Soft = 1, Hard = 2;
        // The pub's District block (narrowed from -35.4..10.2; the freed strips are its service yard and parking).
        public const float PubBlockX0 = -31.4f, PubBlockX1 = 5.2f;

        private static readonly Dictionary<string, Material> s_mats = new Dictionary<string, Material>();
        private static readonly Dictionary<string, GameObject> s_items = new Dictionary<string, GameObject>();

        [MenuItem("Tools/CreatureExperiment/Eateries/Build Fast Food + Pub")]
        public static void BuildAll()
        {
            BuildItemPrefabs();
            BuildFastFood();
            BuildPub();
            var blocks = GameObject.Find("District/Buildings_Blockout");
            if (blocks != null)
            {
                DistrictBlockoutBuilder.CarveAroundInterior(blocks.transform.Find("Blockout_FastFoodStore"),
                    "Street/StoreShells/FastFoodStore", "FF_", -36.2f, -10.7f, -53.6f, -37.4f, 6f);
                DistrictBlockoutBuilder.CarveAroundInterior(blocks.transform.Find("Blockout_PubBar"),
                    "Street/StoreShells/PubBar", "Pub_", PubBlockX0, PubBlockX1, -24.3f, -13.1f, 7f);
            }
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[Eateries] Fast Food + Pub built.");
        }

        // =============================================================================================================
        // Materials
        // =============================================================================================================

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        private static Material Existing(string name)
        {
            if (s_mats.TryGetValue(name, out var m) && m != null)
                return m;
            foreach (string guid in AssetDatabase.FindAssets(name + " t:Material"))
            {
                var found = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (found != null && found.name == name)
                    return s_mats[name] = found;
            }
            Debug.LogWarning("[Eateries] material not found: " + name);
            return null;
        }

        private static Material MakeMat(string name, string shader, Color c, float smooth, float metal, Material copyFrom = null)
        {
            if (s_mats.TryGetValue(name, out var cached) && cached != null)
                return cached;
            EnsureFolder(MatDir);
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = copyFrom != null ? new Material(copyFrom) : new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            return s_mats[name] = m;
        }

        private static Material Lit(string name, float r, float g, float b, float smooth = 0.25f, float metal = 0f)
            => MakeMat(name, "Universal Render Pipeline/Lit", new Color(r, g, b, 1f), smooth, metal);

        private static Material Glow(string name, float r, float g, float b)
            => MakeMat(name, "Universal Render Pipeline/Unlit", new Color(r, g, b, 1f), 0f, 0f);

        private static Material Clear(string name, float r, float g, float b, float a)
            => MakeMat(name, null, new Color(r, g, b, a), 0.9f, 0f, Existing("Mat_Glass_Clear"));

        // =============================================================================================================
        // Primitive helpers
        // =============================================================================================================

        private static GameObject Prim(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale,
            Material mat, bool collider = false, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!collider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = localScale;
            if (mat != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // A box between two world corners (parents here are all at the origin, unrotated).
        private static GameObject Box(Transform parent, string name, float x0, float x1, float y0, float y1, float z0, float z1,
            Material mat, bool collider = true)
            => Prim(parent, PrimitiveType.Cube, name, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(x1 - x0, y1 - y0, z1 - z0), mat, collider);

        // A vertical cylinder standing on y0 (world).
        private static GameObject Cyl(Transform parent, string name, float x, float z, float y0, float height, float diameter,
            Material mat, bool collider = false)
            => Prim(parent, PrimitiveType.Cylinder, name, new Vector3(x, y0 + height * 0.5f, z),
                new Vector3(diameter, height * 0.5f, diameter), mat, collider);

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Transform At(Transform parent, string name, Vector3 worldPos, float yaw = 0f)
        {
            var t = Child(parent, name);
            t.SetPositionAndRotation(worldPos, Quaternion.Euler(0f, yaw, 0f));
            return t;
        }

        /// <summary>World text readable by someone standing on the <paramref name="facing"/> side.</summary>
        private static TextMesh Text(Transform parent, string name, string text, Vector3 pos, Vector3 facing, float charSize, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-facing, Vector3.up));
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = anchor;
            tm.alignment = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.MiddleLeft ? TextAlignment.Left : TextAlignment.Center;
            tm.fontSize = 80;
            tm.characterSize = charSize;
            tm.color = color;
            go.AddComponent<WorldTextFont>();
            return tm;
        }

        private static Light PointLight(Transform parent, string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        private static void Set(Object target, string prop, object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(prop);
            if (p == null)
            {
                Debug.LogError($"[Eateries] {target.GetType().Name}.{prop} not found");
                return;
            }
            switch (value)
            {
                case string s: p.stringValue = s; break;
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
                case bool b: p.boolValue = b; break;
                case Vector2 v2: p.vector2Value = v2; break;
                case Vector3 v3: p.vector3Value = v3; break;
                case Color c: p.colorValue = c; break;
                case string[] arr:
                    p.arraySize = arr.Length;
                    for (int k = 0; k < arr.Length; k++)
                        p.GetArrayElementAtIndex(k).stringValue = arr[k];
                    break;
                case Object[] objs:
                    p.arraySize = objs.Length;
                    for (int k = 0; k < objs.Length; k++)
                        p.GetArrayElementAtIndex(k).objectReferenceValue = objs[k];
                    break;
                case Object o: p.objectReferenceValue = o; break;
                case null: p.objectReferenceValue = null; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // =============================================================================================================
        // Item prefabs
        // =============================================================================================================

        private static T Fx<T>(string name) where T : Component
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FX/" + name + ".prefab");
            return go != null ? go.GetComponent<T>() : null;
        }

        private static GameObject FxGo(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FX/" + name + ".prefab");

        // Clear glass bits for the pub glasses (a copy of the beer bottle's shard burst, re-coloured).
        private static EggSplat ClearShards()
        {
            const string path = "Assets/Prefabs/FX/GlassShards_Clear.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                AssetDatabase.CopyAsset("Assets/Prefabs/FX/GlassShards_Beer.prefab", path);
            var root = PrefabUtility.LoadPrefabContents(path);
            Material shard = Clear("MAT_Glass_Shard", 0.86f, 0.95f, 1f, 0.6f);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer))
                    r.sharedMaterial = shard;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<EggSplat>();
        }

        private static GameObject NewItem(string name) => new GameObject(name);

        private static void Outline(GameObject root, PrimitiveType type, Vector3 center, Vector3 size)
        {
            var o = Prim(root.transform, type, "Outline", center,
                type == PrimitiveType.Cylinder ? new Vector3(size.x, size.y * 0.5f, size.z) : size, Existing("Outline_White"));
            o.GetComponent<MeshRenderer>().enabled = false;
        }

        private static void ItemBasics(GameObject root, string display, string id, int tags, int impact, float mass,
            Vector3 holdPos, Vector3 holdRot, bool continuous)
        {
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.collisionDetectionMode = continuous ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;
            var it = root.AddComponent<Interactable>();
            Set(it, "displayName", display);
            Set(it, "itemId", id);
            Set(it, "tags", tags);
            Set(it, "impactClass", impact);
            Set(it, "discardable", true);
            Set(it, "holdPositionOffset", holdPos);
            Set(it, "holdRotationOffset", holdRot);
            var outline = root.transform.Find("Outline");
            Set(it, "outlineRenderer", outline != null ? outline.GetComponent<Renderer>() : null);
        }

        private static void Food(GameObject root, Color crumbs)
        {
            var food = root.AddComponent<ReadyToEatFood>();
            Set(food, "readyToEat", true);
            Set(food, "crumbColor", crumbs);
        }

        private static void Drink(GameObject root, string emptyName, string emptyId, float emptyMass, GameObject contents,
            string splash, Color spill, bool glass, EggSplat shards)
        {
            var d = root.AddComponent<DrinkContainer>();
            Set(d, "full", true);
            Set(d, "emptyName", emptyName);
            Set(d, "emptyItemId", emptyId);
            Set(d, "emptyMass", emptyMass);
            Set(d, "contents", contents);
            Set(d, "seal", null);
            Set(d, "openedMark", null);
            Set(d, "splashTemplate", Fx<EggSplat>(splash));
            Set(d, "glass", glass);
            Set(d, "breakSpeed", 6.5f); // an F throw (8 m/s) shatters a pub glass
            Set(d, "shardsTemplate", shards);
            Set(d, "spillTemplate", FxGo("DrinkSpill"));
            Set(d, "spillColor", spill);
            root.AddComponent<RollingDamping>();
        }

        private static void Plate(GameObject root)
        {
            var b = root.AddComponent<Breakable>();
            Set(b, "shardMaterial", Existing("MAT_Ceramic_Cup"));
            Set(b, "sound", (int)SfxKind.CeramicBreak);
        }

        private static GameObject Save(GameObject root, string dir)
        {
            EnsureFolder(dir);
            string path = dir + "/" + root.name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            s_items[prefab.name] = prefab;
            return prefab;
        }

        public static void BuildItemPrefabs()
        {
            s_items.Clear();
            Material ffRed = Lit("MAT_FF_Red", 0.82f, 0.13f, 0.1f, 0.35f);
            Material ffYellow = Lit("MAT_FF_Yellow", 1f, 0.78f, 0.16f, 0.3f);
            Material ffGreen = Lit("MAT_FF_CupGreen", 0.18f, 0.62f, 0.3f, 0.35f);
            Material paper = Existing("MAT_Cafe_CupPaper");
            Material bun = Lit("MAT_Food_Bun", 0.86f, 0.56f, 0.24f, 0.35f);
            Material patty = Lit("MAT_Food_Patty", 0.3f, 0.16f, 0.09f, 0.3f);
            Material cheese = Lit("MAT_Food_Cheese", 1f, 0.76f, 0.18f, 0.4f);
            Material lettuce = Lit("MAT_Food_Lettuce", 0.42f, 0.74f, 0.24f, 0.3f);
            Material tomato = Lit("MAT_Food_Tomato", 0.86f, 0.18f, 0.12f, 0.5f);
            Material shrimp = Lit("MAT_Food_ShrimpPatty", 0.96f, 0.55f, 0.24f, 0.25f);
            Material sauceWhite = Lit("MAT_Food_Tartar", 0.96f, 0.94f, 0.84f, 0.4f);
            Material fries = Lit("MAT_Food_Fries", 0.98f, 0.84f, 0.42f, 0.25f);
            Material fried = Lit("MAT_Food_Fried", 0.78f, 0.47f, 0.17f, 0.3f);
            Material onion = Lit("MAT_Food_Onion", 0.95f, 0.88f, 0.7f, 0.3f);
            Material yangnyeom = Lit("MAT_Food_Yangnyeom", 0.68f, 0.12f, 0.05f, 0.7f);
            Material nacho = Lit("MAT_Food_Nacho", 0.98f, 0.77f, 0.3f, 0.2f);
            Material salsa = Lit("MAT_Food_Salsa", 0.78f, 0.14f, 0.07f, 0.5f);
            Material lemon = Lit("MAT_Food_Lemon", 1f, 0.9f, 0.3f, 0.4f);
            Material lime = Lit("MAT_Food_Lime", 0.55f, 0.8f, 0.2f, 0.4f);
            Material sesame = Lit("MAT_Food_Sesame", 0.97f, 0.93f, 0.8f, 0.2f);
            Material grill = Lit("MAT_Food_GrillMark", 0.18f, 0.08f, 0.04f, 0.2f);
            Material sausage = Existing("MAT_Food_Sausage");
            Material mustard = Existing("MAT_Food_Mustard");
            Material ceramic = Existing("MAT_Ceramic_Cup");
            Material glass = Existing("Mat_Glass_Clear");
            Material beer = Lit("MAT_Drink_BeerLiquid", 0.95f, 0.66f, 0.14f, 0.7f);
            Material foam = Lit("MAT_Drink_Foam", 0.98f, 0.96f, 0.9f, 0.3f);
            Material whisky = Lit("MAT_Drink_Whisky", 0.7f, 0.36f, 0.08f, 0.8f);
            Material cocktail = Lit("MAT_Drink_Cocktail", 0.95f, 0.34f, 0.5f, 0.8f);
            Material ice = Clear("MAT_Ice", 0.88f, 0.96f, 1f, 0.55f);
            Material black = Existing("MAT_Cafe_LidBlack");
            EggSplat clearShards = ClearShards();
            var rnd = new System.Random(7);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

            Vector3 handFood = new Vector3(0f, -0.07f, 0.1f), handFoodRot = new Vector3(-20f, -15f, 0f);
            Vector3 handDrink = new Vector3(0f, 0.03f, 0.08f), handDrinkRot = new Vector3(-8f, -10f, 0f);
            Vector3 handPlate = new Vector3(0f, -0.09f, 0.13f), handPlateRot = new Vector3(-12f, 0f, 0f);

            // ---- Fast food -------------------------------------------------------------------------------------
            {
                var go = NewItem("Fries");
                Prim(go.transform, PrimitiveType.Cube, "Carton", new Vector3(0f, 0.045f, 0f), new Vector3(0.095f, 0.09f, 0.05f), ffRed);
                Prim(go.transform, PrimitiveType.Cube, "Logo", new Vector3(0f, 0.05f, -0.026f), new Vector3(0.05f, 0.03f, 0.002f), ffYellow);
                for (int i = 0; i < 11; i++)
                    Prim(go.transform, PrimitiveType.Cube, "Fry", new Vector3(R(-0.036f, 0.036f), R(0.09f, 0.105f), R(-0.014f, 0.014f)),
                        new Vector3(0.011f, 0.07f, 0.011f), fries, false, new Vector3(R(-12f, 12f), R(0f, 90f), R(-14f, 14f)));
                go.AddComponent<BoxCollider>().size = new Vector3(0.1f, 0.135f, 0.06f);
                go.GetComponent<BoxCollider>().center = new Vector3(0f, 0.0675f, 0f);
                Outline(go, PrimitiveType.Cube, new Vector3(0f, 0.0675f, 0f), new Vector3(0.106f, 0.141f, 0.066f));
                ItemBasics(go, "감자튀김", "Fries", TagFood, Soft, 0.15f, handFood, handFoodRot, false);
                Food(go, new Color(0.98f, 0.84f, 0.42f));
                Save(go, FastFoodDir);
            }
            {
                var go = NewItem("OnionRings");
                Prim(go.transform, PrimitiveType.Cube, "Boat", new Vector3(0f, 0.018f, 0f), new Vector3(0.15f, 0.036f, 0.1f), ffYellow);
                for (int i = 0; i < 4; i++)
                {
                    var p = new Vector3(-0.048f + i * 0.032f, 0.048f, (i % 2 == 0 ? 0.008f : -0.008f));
                    var e = new Vector3(70f, 0f, R(-8f, 8f));
                    Prim(go.transform, PrimitiveType.Cylinder, "Ring", p, new Vector3(0.066f, 0.007f, 0.066f), fried, false, e);
                    Prim(go.transform, PrimitiveType.Cylinder, "Hole", p, new Vector3(0.03f, 0.0075f, 0.03f), onion, false, e);
                }
                go.AddComponent<BoxCollider>().size = new Vector3(0.15f, 0.085f, 0.1f);
                go.GetComponent<BoxCollider>().center = new Vector3(0f, 0.0425f, 0f);
                Outline(go, PrimitiveType.Cube, new Vector3(0f, 0.0425f, 0f), new Vector3(0.156f, 0.091f, 0.106f));
                ItemBasics(go, "어니언링", "OnionRings", TagFood, Soft, 0.15f, handFood, handFoodRot, false);
                Food(go, new Color(0.78f, 0.47f, 0.17f));
                Save(go, FastFoodDir);
            }
            void Burger(string name, string display, bool shrimpBurger)
            {
                var go = NewItem(name);
                var t = go.transform;
                Prim(t, PrimitiveType.Cylinder, "BunBottom", new Vector3(0f, 0.0125f, 0f), new Vector3(0.11f, 0.0125f, 0.11f), bun);
                if (shrimpBurger)
                {
                    Prim(t, PrimitiveType.Cylinder, "ShrimpPatty", new Vector3(0f, 0.037f, 0f), new Vector3(0.12f, 0.012f, 0.12f), shrimp);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * 60f * Mathf.Deg2Rad;
                        Prim(t, PrimitiveType.Sphere, "Crumb", new Vector3(Mathf.Cos(a) * 0.05f, 0.04f, Mathf.Sin(a) * 0.05f),
                            new Vector3(0.022f, 0.02f, 0.022f), shrimp);
                    }
                    Prim(t, PrimitiveType.Cylinder, "Tartar", new Vector3(0f, 0.051f, 0f), new Vector3(0.1f, 0.003f, 0.1f), sauceWhite);
                    Prim(t, PrimitiveType.Cylinder, "Lettuce", new Vector3(0f, 0.056f, 0f), new Vector3(0.122f, 0.003f, 0.122f), lettuce);
                }
                else
                {
                    Prim(t, PrimitiveType.Cylinder, "Patty", new Vector3(0f, 0.034f, 0f), new Vector3(0.116f, 0.009f, 0.116f), patty);
                    Prim(t, PrimitiveType.Cube, "Cheese", new Vector3(0f, 0.045f, 0f), new Vector3(0.1f, 0.004f, 0.1f), cheese, false, new Vector3(0f, 45f, 0f));
                    Prim(t, PrimitiveType.Cylinder, "Lettuce", new Vector3(0f, 0.05f, 0f), new Vector3(0.122f, 0.003f, 0.122f), lettuce);
                    Prim(t, PrimitiveType.Cylinder, "Tomato", new Vector3(0f, 0.056f, 0f), new Vector3(0.1f, 0.004f, 0.1f), tomato);
                }
                Prim(t, PrimitiveType.Sphere, "BunTop", new Vector3(0f, 0.072f, 0f), new Vector3(0.114f, 0.055f, 0.114f), bun);
                for (int i = 0; i < 7; i++)
                    Prim(t, PrimitiveType.Cube, "Sesame", new Vector3(R(-0.03f, 0.03f), 0.098f, R(-0.03f, 0.03f)),
                        new Vector3(0.006f, 0.003f, 0.004f), sesame, false, new Vector3(0f, R(0f, 180f), 0f));
                go.AddComponent<BoxCollider>().size = new Vector3(0.12f, 0.1f, 0.12f);
                go.GetComponent<BoxCollider>().center = new Vector3(0f, 0.05f, 0f);
                Outline(go, PrimitiveType.Cylinder, new Vector3(0f, 0.05f, 0f), new Vector3(0.13f, 0.106f, 0.13f));
                ItemBasics(go, display, name, TagFood, Soft, 0.25f, handFood, handFoodRot, false);
                Food(go, new Color(0.86f, 0.56f, 0.24f));
                Save(go, FastFoodDir);
            }
            Burger("BeefBurger", "비프버거", false);
            Burger("ShrimpBurger", "새우버거", true);

            void FountainCup(string name, string display, Material cup, string splash, Color spill)
            {
                var go = NewItem(name);
                var t = go.transform;
                Prim(t, PrimitiveType.Cylinder, "Cup", new Vector3(0f, 0.075f, 0f), new Vector3(0.09f, 0.075f, 0.09f), cup);
                Prim(t, PrimitiveType.Cylinder, "Band", new Vector3(0f, 0.085f, 0f), new Vector3(0.0915f, 0.02f, 0.0915f), paper);
                Prim(t, PrimitiveType.Cylinder, "Lid", new Vector3(0f, 0.153f, 0f), new Vector3(0.096f, 0.006f, 0.096f), paper);
                Prim(t, PrimitiveType.Cylinder, "Straw", new Vector3(0.012f, 0.19f, 0f), new Vector3(0.008f, 0.045f, 0.008f), ffYellow, false, new Vector3(0f, 0f, -8f));
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.046f;
                cap.height = 0.16f;
                cap.center = new Vector3(0f, 0.08f, 0f);
                Outline(go, PrimitiveType.Cylinder, new Vector3(0f, 0.08f, 0f), new Vector3(0.1f, 0.166f, 0.1f));
                ItemBasics(go, display, name, TagDrink, Soft, 0.35f, handDrink, handDrinkRot, true);
                Drink(go, "빈 컵", "EmptyFastFoodCup", 0.02f, null, splash, spill, false, null);
                Save(go, FastFoodDir);
            }
            FountainCup("FountainCola", "콜라", ffRed, "DrinkSplash_Dark", new Color(0.17f, 0.07f, 0.03f));
            FountainCup("FountainSoda", "사이다", ffGreen, "DrinkSplash_Clear", new Color(0.85f, 0.92f, 0.85f));

            // ---- Pub drinks (glass) ----------------------------------------------------------------------------
            {
                var go = NewItem("DraftBeer");
                var t = go.transform;
                Prim(t, PrimitiveType.Cylinder, "Glass", new Vector3(0f, 0.08f, 0f), new Vector3(0.095f, 0.08f, 0.095f), glass);
                Prim(t, PrimitiveType.Cylinder, "GlassBase", new Vector3(0f, 0.006f, 0f), new Vector3(0.096f, 0.006f, 0.096f), glass);
                Prim(t, PrimitiveType.Cube, "Handle", new Vector3(0.068f, 0.085f, 0f), new Vector3(0.012f, 0.09f, 0.02f), glass);
                Prim(t, PrimitiveType.Cube, "HandleTop", new Vector3(0.056f, 0.126f, 0f), new Vector3(0.026f, 0.012f, 0.02f), glass);
                Prim(t, PrimitiveType.Cube, "HandleBottom", new Vector3(0.056f, 0.044f, 0f), new Vector3(0.026f, 0.012f, 0.02f), glass);
                var contents = Child(t, "Contents");
                Prim(contents, PrimitiveType.Cylinder, "Beer", new Vector3(0f, 0.068f, 0f), new Vector3(0.085f, 0.058f, 0.085f), beer);
                Prim(contents, PrimitiveType.Cylinder, "Foam", new Vector3(0f, 0.14f, 0f), new Vector3(0.087f, 0.014f, 0.087f), foam);
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.05f;
                cap.height = 0.165f;
                cap.center = new Vector3(0f, 0.0825f, 0f);
                Outline(go, PrimitiveType.Cylinder, new Vector3(0f, 0.082f, 0f), new Vector3(0.106f, 0.17f, 0.106f));
                ItemBasics(go, "맥주 500cc", "DraftBeer", TagDrink, Hard, 0.8f, handDrink, handDrinkRot, true);
                Drink(go, "빈 맥주잔", "EmptyBeerMug", 0.35f, contents.gameObject, "DrinkSplash_Orange", new Color(0.72f, 0.5f, 0.12f), true, clearShards);
                Save(go, PubDir);
            }
            {
                var go = NewItem("WhiskyRocks");
                var t = go.transform;
                Prim(t, PrimitiveType.Cylinder, "Glass", new Vector3(0f, 0.045f, 0f), new Vector3(0.085f, 0.045f, 0.085f), glass);
                Prim(t, PrimitiveType.Cylinder, "GlassBase", new Vector3(0f, 0.008f, 0f), new Vector3(0.086f, 0.008f, 0.086f), glass);
                var contents = Child(t, "Contents");
                Prim(contents, PrimitiveType.Cylinder, "Whisky", new Vector3(0f, 0.036f, 0f), new Vector3(0.076f, 0.021f, 0.076f), whisky);
                Prim(contents, PrimitiveType.Cube, "Ice1", new Vector3(0.012f, 0.056f, 0.008f), new Vector3(0.032f, 0.032f, 0.032f), ice, false, new Vector3(15f, 30f, 10f));
                Prim(contents, PrimitiveType.Cube, "Ice2", new Vector3(-0.014f, 0.052f, -0.01f), new Vector3(0.03f, 0.03f, 0.03f), ice, false, new Vector3(-10f, 60f, 20f));
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.045f;
                cap.height = 0.095f;
                cap.center = new Vector3(0f, 0.0475f, 0f);
                Outline(go, PrimitiveType.Cylinder, new Vector3(0f, 0.047f, 0f), new Vector3(0.095f, 0.1f, 0.095f));
                ItemBasics(go, "위스키 온더락", "WhiskyRocks", TagDrink, Hard, 0.45f, handDrink, handDrinkRot, true);
                Drink(go, "빈 위스키 잔", "EmptyWhiskyGlass", 0.3f, contents.gameObject, "DrinkSplash_Orange", new Color(0.6f, 0.32f, 0.08f), true, clearShards);
                Save(go, PubDir);
            }
            {
                var go = NewItem("Cocktail");
                var t = go.transform;
                Prim(t, PrimitiveType.Cylinder, "Glass", new Vector3(0f, 0.075f, 0f), new Vector3(0.07f, 0.075f, 0.07f), glass);
                Prim(t, PrimitiveType.Cylinder, "GlassBase", new Vector3(0f, 0.006f, 0f), new Vector3(0.071f, 0.006f, 0.071f), glass);
                var contents = Child(t, "Contents");
                Prim(contents, PrimitiveType.Cylinder, "Liquid", new Vector3(0f, 0.07f, 0f), new Vector3(0.062f, 0.06f, 0.062f), cocktail);
                Prim(contents, PrimitiveType.Cylinder, "Lime", new Vector3(0.032f, 0.15f, 0f), new Vector3(0.045f, 0.004f, 0.045f), lime, false, new Vector3(0f, 0f, 80f));
                Prim(contents, PrimitiveType.Cylinder, "Straw", new Vector3(-0.012f, 0.165f, 0.006f), new Vector3(0.007f, 0.05f, 0.007f), black, false, new Vector3(0f, 0f, -10f));
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.036f;
                cap.height = 0.15f;
                cap.center = new Vector3(0f, 0.075f, 0f);
                Outline(go, PrimitiveType.Cylinder, new Vector3(0f, 0.075f, 0f), new Vector3(0.08f, 0.156f, 0.08f));
                ItemBasics(go, "칵테일", "Cocktail", TagDrink, Hard, 0.4f, handDrink, handDrinkRot, true);
                Drink(go, "빈 칵테일 잔", "EmptyCocktailGlass", 0.25f, contents.gameObject, "DrinkSplash_Clear", new Color(0.85f, 0.3f, 0.45f), true, clearShards);
                Save(go, PubDir);
            }

            // ---- Pub food (on a ceramic plate) -----------------------------------------------------------------
            GameObject PlateFood(string name, out Transform t)
            {
                var go = NewItem(name);
                t = go.transform;
                Prim(t, PrimitiveType.Cylinder, "Plate", new Vector3(0f, 0.007f, 0f), new Vector3(0.22f, 0.007f, 0.22f), ceramic);
                Prim(t, PrimitiveType.Cylinder, "PlateRim", new Vector3(0f, 0.012f, 0f), new Vector3(0.226f, 0.002f, 0.226f), ceramic);
                return go;
            }
            void FinishPlate(GameObject go, string display, Color crumbs)
            {
                go.AddComponent<BoxCollider>().size = new Vector3(0.22f, 0.06f, 0.22f);
                go.GetComponent<BoxCollider>().center = new Vector3(0f, 0.03f, 0f);
                Outline(go, PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f), new Vector3(0.232f, 0.064f, 0.232f));
                ItemBasics(go, display, go.name, TagFood, Hard, 0.6f, handPlate, handPlateRot, true);
                Food(go, crumbs);
                Plate(go);
                Save(go, PubDir);
            }
            void ChickenPieces(Transform t, Material m, bool seeds)
            {
                Prim(t, PrimitiveType.Cube, "Liner", new Vector3(0f, 0.015f, 0f), new Vector3(0.15f, 0.002f, 0.15f), paper, false, new Vector3(0f, 20f, 0f));
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 60f * Mathf.Deg2Rad + R(-0.2f, 0.2f);
                    float rr = i == 0 ? 0f : 0.055f;
                    if (i == 0) a = 0f;
                    var pos = new Vector3(Mathf.Cos(a) * rr, i == 0 ? 0.045f : 0.033f, Mathf.Sin(a) * rr);
                    if (i % 2 == 0)
                        Prim(t, PrimitiveType.Capsule, "Drumstick", pos, new Vector3(0.042f, 0.035f, 0.036f), m, false, new Vector3(90f, R(0f, 180f), 0f));
                    else
                        Prim(t, PrimitiveType.Sphere, "Piece", pos, new Vector3(0.06f, 0.036f, 0.05f), m, false, new Vector3(R(-10f, 10f), R(0f, 180f), 0f));
                }
                if (seeds)
                    for (int i = 0; i < 12; i++)
                        Prim(t, PrimitiveType.Cube, "Sesame", new Vector3(R(-0.06f, 0.06f), 0.055f, R(-0.06f, 0.06f)),
                            new Vector3(0.006f, 0.003f, 0.004f), sesame, false, new Vector3(0f, R(0f, 180f), 0f));
            }
            void FriesPile(Transform t, Vector3 at, int count)
            {
                for (int i = 0; i < count; i++)
                    Prim(t, PrimitiveType.Cube, "Fry", at + new Vector3(R(-0.025f, 0.025f), 0.018f + i * 0.0025f, R(-0.025f, 0.025f)),
                        new Vector3(0.07f, 0.011f, 0.011f), fries, false, new Vector3(0f, R(0f, 180f), R(-6f, 6f)));
            }
            {
                var go = PlateFood("FriedChicken", out var t);
                ChickenPieces(t, fried, false);
                FinishPlate(go, "프라이드 치킨", new Color(0.78f, 0.47f, 0.17f));
            }
            {
                var go = PlateFood("YangnyeomChicken", out var t);
                ChickenPieces(t, yangnyeom, true);
                FinishPlate(go, "양념 치킨", new Color(0.68f, 0.12f, 0.05f));
            }
            {
                var go = PlateFood("NachosSalsa", out var t);
                for (int i = 0; i < 12; i++)
                {
                    float a = R(0f, Mathf.PI * 2f), rr = R(0f, 0.06f);
                    Prim(t, PrimitiveType.Cube, "Chip", new Vector3(Mathf.Cos(a) * rr - 0.02f, 0.02f + i * 0.0025f, Mathf.Sin(a) * rr - 0.01f),
                        new Vector3(0.05f, 0.004f, 0.05f), nacho, false, new Vector3(R(-18f, 18f), 45f + R(-30f, 30f), R(-18f, 18f)));
                }
                for (int i = 0; i < 4; i++)
                    Prim(t, PrimitiveType.Cube, "CheeseDrizzle", new Vector3(R(-0.05f, 0.02f), 0.05f, R(-0.04f, 0.03f)),
                        new Vector3(0.06f, 0.003f, 0.006f), cheese, false, new Vector3(0f, R(0f, 180f), 0f));
                Prim(t, PrimitiveType.Cylinder, "SalsaBowl", new Vector3(0.06f, 0.026f, 0.05f), new Vector3(0.065f, 0.014f, 0.065f), ceramic);
                Prim(t, PrimitiveType.Cylinder, "Salsa", new Vector3(0.06f, 0.04f, 0.05f), new Vector3(0.056f, 0.002f, 0.056f), salsa);
                FinishPlate(go, "나쵸 & 살사", new Color(0.98f, 0.77f, 0.3f));
            }
            {
                var go = PlateFood("FishAndChips", out var t);
                Prim(t, PrimitiveType.Capsule, "Fish1", new Vector3(-0.03f, 0.03f, -0.02f), new Vector3(0.045f, 0.06f, 0.03f), fried, false, new Vector3(0f, 20f, 90f));
                Prim(t, PrimitiveType.Capsule, "Fish2", new Vector3(-0.025f, 0.035f, 0.035f), new Vector3(0.042f, 0.055f, 0.03f), fried, false, new Vector3(0f, -15f, 90f));
                FriesPile(t, new Vector3(0.05f, 0f, -0.01f), 12);
                Prim(t, PrimitiveType.Sphere, "Lemon", new Vector3(0.05f, 0.025f, 0.06f), new Vector3(0.035f, 0.022f, 0.022f), lemon);
                Prim(t, PrimitiveType.Cylinder, "Tartar", new Vector3(-0.065f, 0.022f, 0.055f), new Vector3(0.035f, 0.01f, 0.035f), sauceWhite);
                FinishPlate(go, "피시앤칩스", new Color(0.78f, 0.47f, 0.17f));
            }
            {
                var go = PlateFood("SausageFries", out var t);
                for (int i = 0; i < 3; i++)
                {
                    var p = new Vector3(-0.045f + i * 0.004f, 0.026f, -0.045f + i * 0.04f);
                    Prim(t, PrimitiveType.Capsule, "Sausage", p, new Vector3(0.026f, 0.05f, 0.026f), sausage, false, new Vector3(0f, 10f * i, 90f));
                    for (int k = 0; k < 3; k++)
                        Prim(t, PrimitiveType.Cube, "GrillMark", p + new Vector3(-0.025f + k * 0.025f, 0.013f, 0f),
                            new Vector3(0.004f, 0.002f, 0.022f), grill, false, new Vector3(0f, 10f * i + 20f, 0f));
                }
                FriesPile(t, new Vector3(0.055f, 0f, 0f), 12);
                Prim(t, PrimitiveType.Cylinder, "Mustard", new Vector3(0.0f, 0.016f, 0.075f), new Vector3(0.03f, 0.004f, 0.03f), mustard);
                FinishPlate(go, "소시지구이 & 감자튀김", new Color(0.6f, 0.25f, 0.12f));
            }
            AssetDatabase.SaveAssets();
        }

        private static GameObject ItemPrefab(string name)
        {
            if (s_items.TryGetValue(name, out var p) && p != null)
                return p;
            p = AssetDatabase.LoadAssetAtPath<GameObject>(FastFoodDir + "/" + name + ".prefab")
                ?? AssetDatabase.LoadAssetAtPath<GameObject>(PubDir + "/" + name + ".prefab");
            return s_items[name] = p;
        }

        // An inactive prefab instance under the counter's menu root = one menu line (the price lives here, on the shop side).
        private static void MenuLine(Transform menuRoot, string prefab, string menuName, int price, string category, Vector3 at)
        {
            var src = ItemPrefab(prefab);
            if (src == null)
            {
                Debug.LogError("[Eateries] missing item prefab " + prefab);
                return;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, menuRoot);
            go.name = prefab + "_Template";
            go.transform.position = at;
            var entry = go.AddComponent<CafeMenuItem>();
            Set(entry, "menuName", menuName);
            Set(entry, "price", price);
            Set(entry, "category", category);
            go.SetActive(false);
        }

        // =============================================================================================================
        // Shared furniture / NPC
        // =============================================================================================================

        private static Transform ReplaceRoot(string name)
        {
            var shells = GameObject.Find("Street/StoreShells").transform;
            var old = shells.Find(name);
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var root = Child(shells, name);
            root.position = Vector3.zero;
            return root;
        }

        private static SittableChair Seat(Transform parent, string name, Vector3 floorPos, float yaw, float seatHeight, Vector3 colliderSize,
            float colliderCenterY, float eyeAboveSeat)
        {
            var root = At(parent, name, floorPos + Vector3.up * seatHeight, yaw);
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.size = colliderSize;
            box.center = new Vector3(0f, colliderCenterY, 0f);
            var sit = Child(root, "SitPoint");
            sit.localPosition = new Vector3(0f, eyeAboveSeat, -0.05f);
            var stand = Child(root, "StandPoint");
            stand.localPosition = new Vector3(0f, -seatHeight + 0.06f, -0.75f);
            var label = Child(root, "SeatedLabelAnchor");
            label.localPosition = new Vector3(0f, 0.3f, 0.75f);
            var chair = root.gameObject.AddComponent<SittableChair>();
            Set(chair, "sitPoint", sit);
            Set(chair, "standPoint", stand);
            Set(chair, "seatedLabelAnchor", label);
            Set(chair, "player", Object.FindFirstObjectByType<PlayerInteractor>());
            return chair;
        }

        // A plastic fast-food chair facing local +Z.
        private static void FastFoodChair(Transform parent, string name, Vector3 floorPos, float yaw, Material seat, Material leg)
        {
            var c = Seat(parent, name, floorPos, yaw, 0.46f, new Vector3(0.46f, 0.9f, 0.46f), 0f, 0.82f).transform;
            Prim(c, PrimitiveType.Cube, "Seat", new Vector3(0f, 0f, 0f), new Vector3(0.44f, 0.04f, 0.42f), seat);
            Prim(c, PrimitiveType.Cube, "Back", new Vector3(0f, 0.25f, -0.2f), new Vector3(0.44f, 0.42f, 0.03f), seat, false, new Vector3(-6f, 0f, 0f));
            foreach (var p in new[] { new Vector2(-0.19f, -0.17f), new Vector2(0.19f, -0.17f), new Vector2(-0.19f, 0.17f), new Vector2(0.19f, 0.17f) })
                Prim(c, PrimitiveType.Cylinder, "Leg", new Vector3(p.x, -0.23f, p.y), new Vector3(0.025f, 0.23f, 0.025f), leg);
        }

        // A wooden pub chair facing local +Z.
        private static void PubChair(Transform parent, string name, Vector3 floorPos, float yaw, Material wood, Material cushion)
        {
            var c = Seat(parent, name, floorPos, yaw, 0.46f, new Vector3(0.46f, 0.92f, 0.46f), 0f, 0.82f).transform;
            Prim(c, PrimitiveType.Cube, "Seat", Vector3.zero, new Vector3(0.44f, 0.05f, 0.42f), wood);
            Prim(c, PrimitiveType.Cube, "Cushion", new Vector3(0f, 0.03f, 0f), new Vector3(0.38f, 0.02f, 0.36f), cushion);
            Prim(c, PrimitiveType.Cube, "Back", new Vector3(0f, 0.27f, -0.2f), new Vector3(0.42f, 0.36f, 0.04f), wood);
            foreach (var p in new[] { new Vector2(-0.19f, -0.18f), new Vector2(0.19f, -0.18f), new Vector2(-0.19f, 0.18f), new Vector2(0.19f, 0.18f) })
                Prim(c, PrimitiveType.Cube, "Leg", new Vector3(p.x, -0.23f, p.y), new Vector3(0.04f, 0.46f, 0.04f), wood);
        }

        // A high bar stool facing local +Z.
        private static void BarStool(Transform parent, string name, Vector3 floorPos, float yaw, Material leather, Material brass)
        {
            var c = Seat(parent, name, floorPos, yaw, 0.76f, new Vector3(0.4f, 0.8f, 0.4f), -0.36f, 0.76f).transform;
            Prim(c, PrimitiveType.Cylinder, "Seat", new Vector3(0f, 0.02f, 0f), new Vector3(0.38f, 0.035f, 0.38f), leather);
            Prim(c, PrimitiveType.Cylinder, "Pole", new Vector3(0f, -0.38f, 0f), new Vector3(0.05f, 0.38f, 0.05f), brass);
            Prim(c, PrimitiveType.Cylinder, "FootRing", new Vector3(0f, -0.48f, 0f), new Vector3(0.32f, 0.008f, 0.32f), brass);
            Prim(c, PrimitiveType.Cylinder, "Base", new Vector3(0f, -0.75f, 0f), new Vector3(0.36f, 0.012f, 0.36f), brass);
        }

        private struct NpcParts
        {
            public Transform Root, Look, OutlineGroup, ArmL, ArmR;
        }

        // A primitive person facing local +Z (eyes on that side).
        private static NpcParts Npc(Transform parent, string name, Vector3 floorPos, float yaw, Material shirt, Material sleeves,
            Material pants, Material skin)
        {
            Material dark = Existing("MAT_Prop_DarkPlastic");
            Material outline = Existing("Outline_White");
            var n = new NpcParts { Root = At(parent, name, floorPos, yaw) };
            var cap = n.Root.gameObject.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 0.9f, 0f);
            cap.radius = 0.28f;
            cap.height = 1.8f;
            Prim(n.Root, PrimitiveType.Cube, "Leg_L", new Vector3(-0.11f, 0.43f, 0f), new Vector3(0.17f, 0.86f, 0.2f), pants);
            Prim(n.Root, PrimitiveType.Cube, "Leg_R", new Vector3(0.11f, 0.43f, 0f), new Vector3(0.17f, 0.86f, 0.2f), pants);
            Prim(n.Root, PrimitiveType.Cube, "Body", new Vector3(0f, 1.17f, 0f), new Vector3(0.46f, 0.64f, 0.26f), shirt);
            Prim(n.Root, PrimitiveType.Cube, "Head", new Vector3(0f, 1.68f, 0f), new Vector3(0.22f, 0.27f, 0.24f), skin);
            Prim(n.Root, PrimitiveType.Cube, "Eye_L", new Vector3(-0.05f, 1.71f, 0.121f), new Vector3(0.03f, 0.03f, 0.01f), dark);
            Prim(n.Root, PrimitiveType.Cube, "Eye_R", new Vector3(0.05f, 1.71f, 0.121f), new Vector3(0.03f, 0.03f, 0.01f), dark);
            n.ArmL = Child(n.Root, "ArmPivot_L");
            n.ArmL.localPosition = new Vector3(-0.3f, 1.42f, 0f);
            Prim(n.ArmL, PrimitiveType.Cube, "Arm_L", new Vector3(0f, -0.29f, 0f), new Vector3(0.12f, 0.62f, 0.15f), sleeves);
            n.ArmR = Child(n.Root, "ArmPivot_R");
            n.ArmR.localPosition = new Vector3(0.3f, 1.42f, 0f);
            Prim(n.ArmR, PrimitiveType.Cube, "Arm_R", new Vector3(0f, -0.29f, 0f), new Vector3(0.12f, 0.62f, 0.15f), sleeves);
            n.Look = Child(n.Root, "LookTarget");
            n.Look.localPosition = new Vector3(0f, 1.6f, 0f);
            n.OutlineGroup = Child(n.Root, "Outline_" + name);
            foreach (var p in new[] { ("Leg_L", new Vector3(-0.11f, 0.43f, 0f), new Vector3(0.19f, 0.88f, 0.22f)),
                                      ("Leg_R", new Vector3(0.11f, 0.43f, 0f), new Vector3(0.19f, 0.88f, 0.22f)),
                                      ("Body", new Vector3(0f, 1.17f, 0f), new Vector3(0.48f, 0.66f, 0.28f)),
                                      ("Head", new Vector3(0f, 1.68f, 0f), new Vector3(0.24f, 0.29f, 0.26f)) })
                Prim(n.OutlineGroup, PrimitiveType.Cube, "Outline_" + p.Item1, p.Item2, p.Item3, outline).GetComponent<Renderer>().enabled = false;
            return n;
        }

        private static void CounterOutline(Transform parent, Vector3 localCenter, Vector3 size)
        {
            var g = Child(parent, "Outline_Counter");
            Prim(g, PrimitiveType.Cube, "Outline", localCenter, size, Existing("Outline_White")).GetComponent<Renderer>().enabled = false;
        }

        // Hinge on one edge of the opening; the leaf runs along the hinge's local -Z. Opening adds +90° of yaw.
        // leafSize (optional): x = width, y = height, z = gap under the leaf (toilet stall doors).
        private static SwingDoor Door(Transform parent, string name, Vector3 hingePos, float yaw, bool locked, string lockedPrompt,
            bool startOpen = false, string openPrompt = null, Material leafMat = null, Vector3? leafSize = null, float openDelta = 90f,
            bool metalSound = false)
        {
            var src = GameObject.Find("Street/StoreShells/Cafe_Shell/Door_Cafe_Hinge");
            if (src == null)
            {
                Debug.LogError("[Eateries] door template Door_Cafe_Hinge not found");
                return null;
            }
            var go = Object.Instantiate(src, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(hingePos, Quaternion.Euler(0f, yaw, 0f));
            foreach (Transform c in go.transform)
                if (c.name.EndsWith("_Leaf"))
                {
                    c.name = name.Replace("_Hinge", "") + "_Leaf";
                    if (leafSize.HasValue)
                    {
                        Vector3 l = leafSize.Value;
                        c.localPosition = new Vector3(0f, l.z + l.y * 0.5f, -l.x * 0.5f);
                        c.localScale = new Vector3(0.04f, l.y, l.x);
                    }
                    if (leafMat != null)
                        c.GetComponent<Renderer>().sharedMaterial = leafMat;
                }
            var door = go.GetComponent<SwingDoor>();
            // SwingDoor angles are absolute LOCAL yaw (relative to the parent - a locker is rotated): closed = as placed.
            float localYaw = go.transform.localEulerAngles.y;
            Set(door, "closedAngle", localYaw);
            Set(door, "openAngle", localYaw + openDelta);
            Set(door, "locked", locked);
            if (openPrompt != null)
                Set(door, "openPrompt", openPrompt);
            if (metalSound)
                Set(door, "metalSound", true);
            if (startOpen) // propped open; SwingDoor reads the state from the angle in Awake
                go.transform.localRotation = Quaternion.Euler(0f, localYaw + openDelta, 0f);
            return door;
            if (locked)
                Set(door, "lockedPrompt", lockedPrompt);
        }

        private static void Configure(BaristaCounter c, Transform look, Transform menu, Transform pickupPoint, Transform pickupArea,
            TextMesh board, string boardTitle, float prepare, Transform counterOutline, Transform npcOutline, string speaker,
            string greeting, string categoryQuestion, string cancelled, string ordered, string notEnough, string waiting,
            string preparing, string ready)
        {
            Set(c, "baristaLook", look);
            Set(c, "menuRoot", menu);
            Set(c, "pickupPoint", pickupPoint);
            Set(c, "pickupArea", pickupArea);
            Set(c, "menuBoard", board);
            Set(c, "menuBoardTitle", boardTitle);
            Set(c, "prepareSeconds", prepare);
            Set(c, "counterOutline", counterOutline);
            Set(c, "baristaOutline", npcOutline);
            Set(c, "speakerName", speaker);
            Set(c, "greetingLine", greeting);
            Set(c, "categoryQuestionFormat", categoryQuestion);
            Set(c, "cancelledLine", cancelled);
            Set(c, "orderedLine", ordered);
            Set(c, "notEnoughLine", notEnough);
            Set(c, "pickupWaitingLine", waiting);
            Set(c, "preparingLine", preparing);
            Set(c, "readyNotice", ready);
            Set(c, "usePrompt", "E · 주문하기");
        }

        // =============================================================================================================
        // Fast Food Store  (interior x -31..-16, z -53.6..-42.6, door on the south face at x -23.5)
        // =============================================================================================================

        public static void BuildFastFood()
        {
            Material floor = Lit("MAT_FF_Floor", 0.93f, 0.91f, 0.86f, 0.55f);
            Material floorAccent = Lit("MAT_FF_FloorAccent", 0.82f, 0.13f, 0.1f, 0.5f);
            Material wall = Lit("MAT_FF_Wall", 0.97f, 0.96f, 0.93f, 0.2f);
            Material red = Lit("MAT_FF_Red", 0.82f, 0.13f, 0.1f, 0.35f);
            Material yellow = Lit("MAT_FF_Yellow", 1f, 0.78f, 0.16f, 0.3f);
            Material top = Lit("MAT_FF_CounterTop", 0.94f, 0.94f, 0.92f, 0.6f);
            Material steel = Existing("MAT_Metal_Dull");
            Material dark = Existing("MAT_Prop_DarkPlastic");
            Material board = Lit("MAT_FF_MenuBoard", 0.1f, 0.1f, 0.11f, 0.5f);
            Material glass = Existing("Mat_Glass_Clear");
            Material panelGlow = Glow("MAT_FF_LightPanel", 1f, 0.99f, 0.95f);
            Material heatGlow = Glow("MAT_FF_HeatLamp", 1f, 0.55f, 0.2f);
            Material skin = Existing("MAT_Cabinet_Light");
            Material plant = Existing("MAT_Hedge");
            Material pot = Existing("MAT_Bin_Municipal");

            var root = ReplaceRoot("FastFoodStore");
            var shell = Child(root, "Shell");
            float F = G, C = 0.1f; // floor, ceiling underside

            // ---- Shell (FF_* = the interior's bounds for the carve)
            Box(shell, "FF_Floor", -31f, -16f, F, F + 0.01f, -53.6f, -44.4f, floor, false);
            Box(shell, "FF_KitchenFloor", -31f, -16f, F, F + 0.01f, -44.4f, -37.4f, Lit("MAT_FF_KitchenFloor", 0.62f, 0.64f, 0.63f, 0.35f), false);
            Box(shell, "FF_FloorStripe", -24.3f, -22.7f, F + 0.01f, F + 0.012f, -53.4f, -45.1f, floorAccent, false);
            Box(shell, "FF_Ceiling", -31f, -16f, C, C + 0.1f, -53.6f, -37.4f, wall, true);
            Box(shell, "FF_Wall_W", -31f, -30.8f, F, C, -53.6f, -37.4f, wall);
            Box(shell, "FF_Wall_E", -16.2f, -16f, F, C, -53.6f, -37.4f, wall);
            // Back of house (kitchen + storage) runs to the block's north face; back door at x -30.4..-28.8.
            Box(shell, "FF_Wall_Back_W", -31f, -18.0f, F, C, -37.6f, -37.4f, wall);
            Box(shell, "FF_Wall_Back_Lintel", -18.0f, -16.4f, F + 2.2f, C, -37.6f, -37.4f, wall);
            Box(shell, "FF_Wall_Back_E", -16.4f, -16f, F, C, -37.6f, -37.4f, wall);
            // Employee changing room (ex walk-in): x -19.6..-16.2, z -40..-37.6; door from storage at z -39.6..-38.0.
            Box(shell, "FF_Changing_Wall_S", -19.6f, -19.4f, F, C, -40.9f, -39.6f, wall);
            Box(shell, "FF_Changing_Wall_Lintel", -19.6f, -19.4f, F + 2.2f, C, -39.6f, -38.0f, wall);
            Box(shell, "FF_Changing_Wall_N", -19.6f, -19.4f, F, C, -38.0f, -37.6f, wall);
            Box(shell, "FF_Storage_Wall", -28.8f, -19.6f, F, C, -40.2f, -40.0f, wall);
            Box(shell, "FF_Changing_Wall_South", -19.6f, -16.2f, F, C, -41.1f, -40.9f, wall); // changing room reaches 0.9 m further south
            Box(shell, "FF_Wainscot_W", -30.8f, -30.78f, F, F + 0.9f, -53.4f, -42.8f, red, false);
            Box(shell, "FF_Wainscot_E", -16.22f, -16.2f, F, F + 0.9f, -53.4f, -42.8f, red, false);
            // Front: low band, big windows, door, band above.
            float z0 = -53.6f, z1 = -53.4f, sill = F + 0.9f, head = F + 2.5f, doorTop = F + 2.2f;
            Box(shell, "FF_Front_LowW", -31f, -24.3f, F, sill, z0, z1, red);
            Box(shell, "FF_Front_LowE", -22.7f, -16f, F, sill, z0, z1, red);
            Box(shell, "FF_Front_Top", -31f, -16f, head, C, z0, z1, wall);
            Box(shell, "FF_Front_Lintel", -24.3f, -22.7f, doorTop, head, z0, z1, wall);
            Box(shell, "FF_Front_PostW", -31f, -30.2f, sill, head, z0, z1, wall);
            Box(shell, "FF_Front_PostDW", -25.2f, -24.3f, sill, head, z0, z1, wall);
            Box(shell, "FF_Front_PostDE", -22.7f, -21.8f, sill, head, z0, z1, wall);
            Box(shell, "FF_Front_PostE", -16.8f, -16f, sill, head, z0, z1, wall);
            Box(shell, "FF_Window_W", -30.2f, -25.2f, sill, head, -53.52f, -53.48f, glass);
            Box(shell, "FF_Window_E", -21.8f, -16.8f, sill, head, -53.52f, -53.48f, glass);
            // Kitchen partitions either side of the counter line (closes the kitchen off).
            Box(shell, "FF_Partition_W_A", -30.8f, -28.84f, F, C, -44.85f, -44.55f, wall);
            Box(shell, "FF_Partition_W_Lintel", -28.84f, -27.3f, F + 2.2f, C, -44.85f, -44.55f, wall);
            Box(shell, "FF_Partition_W_B", -27.3f, -26.5f, F, C, -44.85f, -44.55f, wall);
            Box(shell, "FF_Partition_E", -20.5f, -16.2f, F, C, -44.85f, -44.55f, wall);

            var doors = Child(root, "Doors");
            Door(doors, "Door_FastFood_Hinge", new Vector3(-22.73f, F, -53.5f), 90f, false, null);
            Door(doors, "Door_Staff_Hinge", new Vector3(-27.3f, F, -44.7f), 90f, false, null, openPrompt: "문 열기 (직원 전용)");
            Door(doors, "Door_Back_Hinge", new Vector3(-16.43f, F, -37.5f), 90f, false, null, openDelta: -90f);
            Door(doors, "Door_StaffRoom_Hinge", new Vector3(-19.5f, F, -38.03f), 0f, false, null, openPrompt: "문 열기 (직원 탈의실)");
            var signs = Child(root, "Signs");
            Box(signs, "StaffDoor_Plate", -28.3f, -27.7f, F + 2.3f, F + 2.48f, -44.91f, -44.89f, dark, false);
            Text(signs, "StaffDoor_Text", "STAFF ONLY", new Vector3(-28f, F + 2.39f, -44.92f), Vector3.back, 0.008f, Color.white);

            // ---- Counters
            var interior = Child(root, "Interior");
            Box(interior, "OrderCounter_Top", -26.55f, -23.45f, F + 1.0f, F + 1.04f, -45.05f, -44.35f, top);
            Box(interior, "Condiment_Counter", -23.45f, -22.15f, F, F + 1.0f, -45.0f, -44.4f, red);
            Box(interior, "Condiment_Top", -23.5f, -22.1f, F + 1.0f, F + 1.04f, -45.05f, -44.35f, top);
            Box(interior, "Napkins", -23.2f, -22.95f, F + 1.04f, F + 1.2f, -44.85f, -44.6f, steel);
            Cyl(interior, "Straws", -22.75f, -44.7f, F + 1.04f, 0.18f, 0.08f, steel);
            Cyl(interior, "Ketchup", -22.45f, -44.7f, F + 1.04f, 0.2f, 0.07f, red);
            Box(interior, "Pickup_Counter", -22.1f, -20.5f, F, F + 1.0f, -45.0f, -44.4f, top);
            Box(interior, "Pickup_Stripe", -22.1f, -20.5f, F + 0.75f, F + 0.85f, -45.01f, -45.0f, yellow, false);
            Box(interior, "Pickup_Top", -22.15f, -20.45f, F + 1.0f, F + 1.04f, -45.05f, -44.35f, top);
            Box(interior, "Pickup_Tray", -21.55f, -21.05f, F + 1.04f, F + 1.05f, -44.9f, -44.55f, red, false);
            var pickupPoint = At(interior, "PickupPoint", new Vector3(-21.3f, F + 1.05f, -44.72f));
            var pickupArea = At(interior, "PickupArea", new Vector3(-21.3f, F + 1.2f, -44.7f));
            pickupArea.localScale = new Vector3(1.5f, 0.32f, 0.6f);
            Box(interior, "PickupSign_Board", -22.0f, -20.6f, F + 2.55f, F + 2.9f, -44.72f, -44.68f, yellow, false);
            Box(interior, "PickupSign_Rod", -21.32f, -21.28f, F + 2.9f, C, -44.71f, -44.69f, steel, false);
            Text(interior, "PickupSign_Text", "PICK UP  픽업", new Vector3(-21.3f, F + 2.725f, -44.73f), Vector3.back, 0.022f, new Color(0.35f, 0.08f, 0.05f));

            // Kitchen line behind the counter.
            Box(interior, "Kitchen_BackCounter", -27.0f, -16.4f, F, F + 0.92f, -42.0f, -41.35f, steel);
            Box(interior, "Fryer", -26.9f, -25.9f, F + 0.92f, F + 1.05f, -41.95f, -41.4f, steel);
            Box(interior, "Fryer_Oil", -26.8f, -26.0f, F + 1.05f, F + 1.055f, -41.9f, -41.45f, Existing("MAT_Food_Broth"), false);
            Box(interior, "Grill", -25.6f, -24.2f, F + 0.92f, F + 0.98f, -41.95f, -41.4f, dark);
            Box(interior, "Warmer", -23.9f, -22.6f, F + 0.92f, F + 1.0f, -41.9f, -41.45f, steel);
            Box(interior, "HeatLamp", -23.9f, -22.6f, F + 1.45f, F + 1.5f, -41.85f, -41.5f, heatGlow, false);
            Box(interior, "SodaMachine", -21.9f, -20.7f, F + 0.92f, F + 1.62f, -41.95f, -41.4f, red);
            Box(interior, "SodaMachine_Face", -21.8f, -20.8f, F + 1.25f, F + 1.55f, -41.96f, -41.95f, yellow, false);
            for (int i = 0; i < 4; i++)
                Cyl(interior, "Nozzle", -21.65f + i * 0.25f, -41.85f, F + 1.1f, 0.06f, 0.04f, steel);
            for (int i = 0; i < 3; i++)
                Cyl(interior, "CupStack", -19.5f + i * 0.18f, -41.65f, F + 0.92f, 0.3f, 0.09f, i == 1 ? Existing("MAT_FF_CupGreen") : red);
            BuildFastFoodBackOfHouse(root, steel, dark, red, yellow, white: Lit("MAT_FF_Wall", 0.97f, 0.96f, 0.93f, 0.2f));

            // Menu boards hung from the ceiling over the service line (the centre one is rewritten from the menu by the
            // counter). Built at the old wall height, then the whole group is lifted 0.3 m (head room under them).
            var boards = Child(interior, "MenuBoards");
            var boardsParent = interior;
            interior = boards;
            Box(interior, "MenuBoard_Left", -29.4f, -26.6f, F + 2.0f, F + 3.25f, -42.8f, -42.76f, board, false);
            Box(interior, "MenuBoard_Centre", -26.4f, -23.6f, F + 2.0f, F + 3.25f, -42.8f, -42.76f, board, false);
            Box(interior, "MenuBoard_Right", -23.4f, -20.6f, F + 2.0f, F + 3.25f, -42.8f, -42.76f, board, false);
            Text(interior, "MenuBoard_Left_Title", "BURGERS", new Vector3(-28f, F + 3.05f, -42.75f), Vector3.back, 0.03f, new Color(1f, 0.8f, 0.2f));
            // A burger "photo" from primitives.
            Prim(interior, PrimitiveType.Cylinder, "Board_BunBottom", new Vector3(-28f, F + 2.4f, -42.74f), new Vector3(0.9f, 0.01f, 0.18f), Existing("MAT_Food_Bun"));
            Prim(interior, PrimitiveType.Cube, "Board_Patty", new Vector3(-28f, F + 2.52f, -42.745f), new Vector3(0.95f, 0.1f, 0.01f), Existing("MAT_Food_Patty"));
            Prim(interior, PrimitiveType.Cube, "Board_Lettuce", new Vector3(-28f, F + 2.6f, -42.745f), new Vector3(1.0f, 0.05f, 0.01f), Existing("MAT_Food_Lettuce"));
            Prim(interior, PrimitiveType.Cube, "Board_BunTop", new Vector3(-28f, F + 2.74f, -42.745f), new Vector3(0.92f, 0.22f, 0.01f), Existing("MAT_Food_Bun"));
            Text(interior, "MenuBoard_Right_Title", "COLD DRINKS", new Vector3(-22f, F + 3.05f, -42.75f), Vector3.back, 0.026f, new Color(1f, 0.8f, 0.2f));
            Prim(interior, PrimitiveType.Cube, "Board_Cup1", new Vector3(-22.45f, F + 2.5f, -42.745f), new Vector3(0.38f, 0.6f, 0.01f), red);
            Prim(interior, PrimitiveType.Cube, "Board_Cup2", new Vector3(-21.55f, F + 2.5f, -42.745f), new Vector3(0.38f, 0.6f, 0.01f), Existing("MAT_FF_CupGreen"));
            var menuText = Text(interior, "MenuBoard_Text", "BURGER STOP", new Vector3(-25f, F + 3.2f, -42.75f), Vector3.back, 0.0115f,
                new Color(0.98f, 0.95f, 0.85f), TextAnchor.UpperCenter);
            foreach (float x in new[] { -29.2f, -26.8f, -26.2f, -23.8f, -23.2f, -20.8f })
                Box(interior, "MenuBoard_Rod", x - 0.015f, x + 0.015f, F + 3.25f, C - 0.3f, -42.79f, -42.77f, steel, false);
            boards.position += Vector3.up * 0.3f;
            interior = boardsParent;

            // ---- Order counter (BaristaCounter: counter front + crew = "E · 주문하기")
            var order = At(root, "OrderCounter", new Vector3(-25f, F, -44.7f));
            Prim(order, PrimitiveType.Cube, "Order_Counter", new Vector3(0f, 0.5f, 0f), new Vector3(3.0f, 1.0f, 0.6f), red, true);
            Prim(order, PrimitiveType.Cube, "Order_Stripe", new Vector3(0f, 0.8f, -0.302f), new Vector3(3.0f, 0.1f, 0.004f), yellow);
            Prim(order, PrimitiveType.Cube, "Order_Register", new Vector3(0.9f, 1.19f, 0.05f), new Vector3(0.4f, 0.3f, 0.35f), steel, true);
            Prim(order, PrimitiveType.Cube, "POS_Screen", new Vector3(0.9f, 1.45f, 0.02f), new Vector3(0.32f, 0.22f, 0.03f), dark, true, new Vector3(15f, 0f, 0f));
            CounterOutline(order, new Vector3(0f, 0.5f, 0f), new Vector3(3.06f, 1.06f, 0.66f));
            var crew = Npc(order, "Crew", new Vector3(-25.3f, F, -43.8f), 180f, red, red, dark, skin);
            Prim(crew.Root, PrimitiveType.Cube, "Cap", new Vector3(0f, 1.84f, 0f), new Vector3(0.24f, 0.06f, 0.26f), red);
            Prim(crew.Root, PrimitiveType.Cube, "CapBrim", new Vector3(0f, 1.82f, 0.16f), new Vector3(0.2f, 0.015f, 0.1f), red);
            Prim(crew.Root, PrimitiveType.Cube, "NameTag", new Vector3(0.12f, 1.3f, 0.131f), new Vector3(0.08f, 0.04f, 0.004f), yellow);
            var sway = crew.Root.gameObject.AddComponent<IdleLoopMotion>();
            Set(sway, "pivot", crew.ArmR);
            Set(sway, "baseEuler", new Vector3(-12f, 0f, 4f));
            Set(sway, "amplitude", new Vector3(4f, 0f, 3f));
            Set(sway, "cyclesPerSecond", 0.35f);
            var sway2 = crew.Root.gameObject.AddComponent<IdleLoopMotion>();
            Set(sway2, "pivot", crew.ArmL);
            Set(sway2, "baseEuler", new Vector3(-8f, 0f, -4f));
            Set(sway2, "amplitude", new Vector3(3f, 0f, 3f));
            Set(sway2, "cyclesPerSecond", 0.35f);
            Set(sway2, "phase", 0.5f);

            var menu = Child(root, "MenuItems");
            Vector3 at = pickupPoint.position + Vector3.up * 0.02f;
            MenuLine(menu, "BeefBurger", "비프버거", 650, "버거·사이드", at);
            MenuLine(menu, "ShrimpBurger", "새우버거", 600, "버거·사이드", at);
            MenuLine(menu, "Fries", "감자튀김", 250, "버거·사이드", at);
            MenuLine(menu, "OnionRings", "어니언링", 300, "버거·사이드", at);
            MenuLine(menu, "FountainCola", "콜라", 200, "음료", at);
            MenuLine(menu, "FountainSoda", "사이다", 200, "음료", at);

            var counter = order.gameObject.AddComponent<BaristaCounter>();
            Configure(counter, crew.Look, menu, pickupPoint, pickupArea, menuText, "BURGER STOP  MENU", 2f,
                order.Find("Outline_Counter"), crew.OutlineGroup, "점원",
                "어서오세요! 주문 도와드릴게요.", "{0} 중에 어떤 걸로 드릴까요?", "감사합니다, 또 오세요!",
                "잠시만 기다려주세요. 픽업대에서 받아가세요.", "잔액이 부족합니다.", "먼저 픽업대의 메뉴를 가져가 주세요.",
                "주문하신 메뉴 준비 중입니다. 잠시만요!", "주문하신 메뉴 나왔습니다. 픽업대에서 받아가세요.");

            // ---- Seating: four 4-seat tables + two 2-seat tables = 20 seats
            var seating = Child(root, "Seating");
            int t = 0;
            void Table(float x, float z, bool four)
            {
                t++;
                var g = Child(seating, "Table_" + t);
                float w = four ? 1.3f : 0.75f, d = 0.8f;
                Box(g, "Top", x - w * 0.5f, x + w * 0.5f, F + 0.72f, F + 0.75f, z - d * 0.5f, z + d * 0.5f, top);
                Box(g, "Edge", x - w * 0.5f, x + w * 0.5f, F + 0.705f, F + 0.72f, z - d * 0.5f, z + d * 0.5f, red, false);
                Cyl(g, "Pedestal", x, z, F, 0.72f, 0.09f, steel, true);
                Cyl(g, "Base", x, z, F, 0.03f, 0.5f, steel);
                float[] xs = four ? new[] { -0.33f, 0.33f } : new[] { 0f };
                int s = 0;
                foreach (float dx in xs)
                {
                    FastFoodChair(g, "Chair_" + (++s), new Vector3(x + dx, F, z - 0.72f), 0f, red, steel);
                    FastFoodChair(g, "Chair_" + (++s), new Vector3(x + dx, F, z + 0.72f), 180f, red, steel);
                }
            }
            Table(-28.6f, -47.6f, true);
            Table(-28.6f, -51.3f, true);
            Table(-26.1f, -49.5f, false);
            Table(-18.4f, -47.6f, true);
            Table(-18.4f, -51.3f, true);
            Table(-20.9f, -49.5f, false);

            // ---- Decor
            var decor = Child(root, "Decor");
            Box(decor, "Poster_W", -30.79f, -30.77f, F + 1.3f, F + 2.3f, -50.8f, -49.2f, yellow, false);
            Text(decor, "Poster_W_Text", "NEW!\n새우버거", new Vector3(-30.76f, F + 1.8f, -50f), Vector3.right, 0.02f, new Color(0.7f, 0.1f, 0.05f));
            Box(decor, "Poster_E", -16.23f, -16.21f, F + 1.3f, F + 2.3f, -50.8f, -49.2f, red, false);
            Text(decor, "Poster_E_Text", "FRESH\n& HOT", new Vector3(-16.24f, F + 1.8f, -50f), Vector3.left, 0.022f, new Color(1f, 0.85f, 0.3f));
            Box(decor, "Bin_Station", -17.4f, -16.4f, F, F + 1.0f, -45.6f, -45.0f, red);
            Box(decor, "Bin_Slot", -17.2f, -16.6f, F + 0.7f, F + 0.8f, -45.61f, -45.6f, dark, false);
            Text(decor, "Bin_Text", "THANK YOU", new Vector3(-16.9f, F + 0.88f, -45.62f), Vector3.back, 0.009f, Color.white);
            foreach (var p in new[] { new Vector2(-30.4f, -53.0f), new Vector2(-16.6f, -53.0f) })
            {
                Cyl(decor, "PlantPot", p.x, p.y, F, 0.45f, 0.4f, pot, true);
                Prim(decor, PrimitiveType.Sphere, "Plant", new Vector3(p.x, F + 0.85f, p.y), new Vector3(0.6f, 0.75f, 0.6f), plant);
            }

            // ---- Lighting: bright, even, cool-white ceiling panels
            var lights = Child(root, "Lighting");
            var white = new Color(1f, 0.97f, 0.92f);
            foreach (float x in new[] { -28.3f, -23.5f, -18.7f })
                foreach (float z in new[] { -47.4f, -51.4f })
                {
                    PointLight(lights, "FFLight", new Vector3(x, C - 0.3f, z), white, 2.4f, 7.5f);
                    Box(lights, "FFPanel", x - 0.6f, x + 0.6f, C - 0.03f, C, z - 0.3f, z + 0.3f, panelGlow, false);
                }
            foreach (var p in new[] { new Vector2(-25.5f, -45.6f), new Vector2(-21.3f, -45.6f), new Vector2(-24f, -43.6f) })
            {
                PointLight(lights, "FFLight_Counter", new Vector3(p.x, C - 0.3f, p.y), white, 1.8f, 5f);
                Box(lights, "FFPanel", p.x - 0.5f, p.x + 0.5f, C - 0.03f, C, p.y - 0.2f, p.y + 0.2f, panelGlow, false);
            }

            // ---- Outside: sign over the door + a warm entrance light
            var outside = Child(root, "Exterior");
            Box(outside, "Sign_Board", -26.5f, -20.5f, F + 2.75f, F + 3.45f, -53.75f, -53.62f, red, false);
            Box(outside, "Sign_Stripe", -26.5f, -20.5f, F + 2.68f, F + 2.75f, -53.75f, -53.62f, yellow, false);
            Text(outside, "Sign_Text", "BURGER STOP  버거스탑", new Vector3(-23.5f, F + 3.1f, -53.77f), Vector3.back, 0.05f, new Color(1f, 0.85f, 0.25f));
            PointLight(outside, "EntranceLight", new Vector3(-23.5f, F + 2.6f, -54.3f), new Color(1f, 0.9f, 0.75f), 1.2f, 4.5f);
        }

        // =============================================================================================================
        // Pub / Bar  (interior x -24..-6, z -23.6..-13.1, door on the north face at x -14.5)
        // =============================================================================================================

        public static void BuildPub()
        {
            Material floor = Lit("MAT_Pub_Floor", 0.3f, 0.18f, 0.1f, 0.45f);
            Material wall = Lit("MAT_Pub_Wall", 0.55f, 0.4f, 0.28f, 0.15f);
            Material ceiling = Lit("MAT_Pub_Ceiling", 0.2f, 0.13f, 0.08f, 0.2f);
            Material woodDark = Lit("MAT_Pub_WoodDark", 0.24f, 0.13f, 0.07f, 0.55f);
            Material woodMid = Lit("MAT_Pub_WoodMid", 0.42f, 0.25f, 0.13f, 0.4f);
            Material brass = Lit("MAT_Pub_Brass", 0.78f, 0.6f, 0.28f, 0.75f, 0.8f);
            Material leather = Lit("MAT_Pub_Leather", 0.38f, 0.09f, 0.06f, 0.5f);
            Material mirror = Lit("MAT_Pub_Mirror", 0.45f, 0.48f, 0.5f, 0.95f, 0.6f);
            Material chalk = Lit("MAT_Pub_Chalkboard", 0.08f, 0.11f, 0.09f, 0.1f);
            Material bottleGreen = Lit("MAT_Pub_BottleGreen", 0.1f, 0.34f, 0.14f, 0.9f);
            Material bottleAmber = Lit("MAT_Pub_BottleAmber", 0.55f, 0.28f, 0.07f, 0.9f);
            Material bottleClear = Clear("MAT_Pub_BottleClear", 0.85f, 0.92f, 0.95f, 0.45f);
            Material bottleRed = Lit("MAT_Pub_BottleRed", 0.45f, 0.06f, 0.08f, 0.9f);
            Material warmGlow = Glow("MAT_Pub_WarmGlow", 1f, 0.72f, 0.38f);
            Material glass = Existing("Mat_Glass_Clear");
            Material shirtWhite = Existing("MAT_Cafe_CupPaper");
            Material dark = Existing("MAT_Prop_DarkPlastic");
            Material skin = Existing("MAT_Cabinet_Light");
            Material paper = Existing("MAT_Cafe_CupPaper");
            Color warm = new Color(1f, 0.72f, 0.45f);

            var root = ReplaceRoot("PubBar");
            var shell = Child(root, "Shell");
            float F = G, C = -0.1f;

            // ---- Shell (Pub_*)
            Box(shell, "Pub_Floor", -24f, -6f, F, F + 0.01f, -23.6f, -13.1f, floor, false);
            Box(shell, "Pub_Ceiling", -24f, -6f, C, C + 0.1f, -23.6f, -13.1f, ceiling, true);
            // Hall west wall: openings for the backroom (z -22.9..-21.3) and the restroom (z -19.2..-17.6).
            Box(shell, "Pub_Wall_W_A", -24f, -23.8f, F, C, -24.3f, -22.9f, wall);
            Box(shell, "Pub_Wall_W_Lintel1", -24f, -23.8f, F + 2.2f, C, -22.9f, -21.3f, wall);
            Box(shell, "Pub_Wall_W_B", -24f, -23.8f, F, C, -21.3f, -19.2f, wall);
            Box(shell, "Pub_Wall_W_Lintel2", -24f, -23.8f, F + 2.2f, C, -19.2f, -17.6f, wall);
            Box(shell, "Pub_Wall_W_C", -24f, -23.8f, F, C, -17.6f, -13.1f, wall);
            Box(shell, "Pub_Wall_E", -6.2f, -6f, F, C, -23.6f, -13.1f, wall);
            Box(shell, "Pub_Wall_Back", -24f, -6f, F, C, -24.3f, -23.4f, wall); // to the block face (the wing makes the bounds reach it)
            float z0 = -13.3f, z1 = -13.1f, sill = F + 0.9f, head = F + 2.2f;
            Box(shell, "Pub_Front_LowW", -24f, -15.3f, F, sill, z0, z1, wall);
            Box(shell, "Pub_Front_LowE", -13.7f, -6f, F, sill, z0, z1, wall);
            Box(shell, "Pub_Front_Top", -24f, -6f, head, C, z0, z1, wall);
            Box(shell, "Pub_Front_Mid1", -24f, -21.5f, sill, head, z0, z1, wall);
            Box(shell, "Pub_Front_Mid2", -19.5f, -15.3f, sill, head, z0, z1, wall);
            Box(shell, "Pub_Front_Mid3", -13.7f, -9.5f, sill, head, z0, z1, wall);
            Box(shell, "Pub_Front_Mid4", -7.5f, -6f, sill, head, z0, z1, wall);
            Box(shell, "Pub_Window_W", -21.5f, -19.5f, sill, head, -13.22f, -13.18f, glass);
            Box(shell, "Pub_Window_E", -9.5f, -7.5f, sill, head, -13.22f, -13.18f, glass);
            foreach (float x in new[] { -21.5f, -19.5f, -9.5f, -7.5f })
                Box(shell, "Pub_WindowFrame", x - 0.04f, x + 0.04f, sill, head, -13.34f, -13.06f, woodDark, false);
            // Wood panelling (lower walls) + ceiling beams.
            foreach (var seg in new[] { (-23.4f, -22.98f), (-21.22f, -19.28f), (-17.52f, -13.3f) })
            {
                Box(shell, "Panel_W", -23.8f, -23.77f, F, F + 1.0f, seg.Item1, seg.Item2, woodMid, false);
                Box(shell, "Panel_Rail_W", -23.8f, -23.74f, F + 1.0f, F + 1.05f, seg.Item1, seg.Item2, woodDark, false);
            }
            Box(shell, "Panel_E", -6.23f, -6.2f, F, F + 1.0f, -23.4f, -13.3f, woodMid, false);
            Box(shell, "Panel_FrontW", -23.8f, -15.42f, F, F + 0.9f, -13.33f, -13.3f, woodMid, false);
            Box(shell, "Panel_FrontE", -13.58f, -6.2f, F, F + 0.9f, -13.33f, -13.3f, woodMid, false);
            Box(shell, "Panel_Rail_E", -6.26f, -6.2f, F + 1.0f, F + 1.05f, -23.4f, -13.3f, woodDark, false);
            for (float x = -22f; x <= -7f; x += 2.5f)
                Box(shell, "Beam", x - 0.12f, x + 0.12f, C - 0.22f, C, -23.4f, -13.3f, woodDark, false);

            var doors = Child(root, "Doors");
            var frontDoor = Door(doors, "Door_Pub_Hinge", new Vector3(-15.27f, F, -13.2f), 270f, false, null);
            Set(frontDoor, "lockedPrompt", "CLOSED · 영업 시간이 아닙니다");
            // Door frame (both faces) + threshold, so the opening reads as a doorway, not a gap in the wall.
            foreach (var face in new[] { (-13.42f, -13.3f, "In"), (-13.1f, -12.98f, "Out") })
            {
                Box(doors, "DoorFrame_" + face.Item3 + "_W", -15.44f, -15.3f, F, F + 2.34f, face.Item1, face.Item2, woodDark, false);
                Box(doors, "DoorFrame_" + face.Item3 + "_E", -13.7f, -13.56f, F, F + 2.34f, face.Item1, face.Item2, woodDark, false);
                Box(doors, "DoorFrame_" + face.Item3 + "_Head", -15.44f, -13.56f, F + 2.2f, F + 2.34f, face.Item1, face.Item2, woodDark, false);
            }
            Box(doors, "DoorThreshold", -15.3f, -13.7f, F, F + 0.012f, -13.42f, -12.98f, brass, false);
            Box(doors, "DoorMat", -15.1f, -13.9f, F, F + 0.01f, -14.6f, -13.6f, leather, false);
            // Hinged on the sink side and swinging into the restroom (-90°): open, the leaf lies along the dead strip
            // by the hand dryer instead of across the way back from the stalls.
            Door(doors, "Door_WC_Hinge", new Vector3(-23.9f, F, -19.17f), 180f, false, null, openPrompt: "화장실 문 열기",
                leafMat: Lit("MAT_WC_Door", 0.14f, 0.3f, 0.22f, 0.35f), openDelta: -90f);
            Door(doors, "Door_Staff_Hinge", new Vector3(-23.9f, F, -21.33f), 0f, false, null, openPrompt: "문 열기 (직원 전용)");
            var rearDoor = Door(doors, "Door_Rear_Hinge", new Vector3(-31.3f, F, -22.47f), 180f, false, null);
            Set(rearDoor, "lockedPrompt", "잠겨 있습니다");
            foreach (var z in new[] { (-22.98f, -22.9f), (-21.3f, -21.22f), (-19.28f, -19.2f), (-17.6f, -17.52f) })
                Box(doors, "HallDoorFrame", -23.8f, -23.68f, F, F + 2.34f, z.Item1, z.Item2, woodDark, false);
            Box(doors, "HallDoorFrame_Head", -23.8f, -23.68f, F + 2.2f, F + 2.34f, -22.98f, -21.22f, woodDark, false);
            Box(doors, "HallDoorFrame_Head", -23.8f, -23.68f, F + 2.2f, F + 2.34f, -19.28f, -17.52f, woodDark, false);
            var signs = Child(root, "Signs");
            Box(signs, "WC_Plate", -23.68f, -23.66f, F + 2.42f, F + 2.66f, -18.85f, -17.95f, woodDark, false);
            Text(signs, "WC_Text", "WC  화장실", new Vector3(-23.65f, F + 2.54f, -18.4f), Vector3.right, 0.011f, new Color(0.98f, 0.9f, 0.7f));
            Box(signs, "Staff_Plate", -23.68f, -23.66f, F + 2.42f, F + 2.66f, -22.65f, -21.55f, woodDark, false);
            Text(signs, "Staff_Text", "STAFF ONLY  직원 전용", new Vector3(-23.65f, F + 2.54f, -22.1f), Vector3.right, 0.0075f, new Color(0.98f, 0.9f, 0.7f));
            // OPEN / CLOSED board beside the front door (text + colour set by PubBusinessHours).
            Box(signs, "OpenSign_Board", -16.75f, -15.75f, F + 1.25f, F + 1.65f, -13.1f, -13.06f, Existing("MAT_Prop_DarkPlastic"), false);
            var openSign = Text(signs, "OpenSign_Text", "OPEN", new Vector3(-16.25f, F + 1.45f, -13.05f), Vector3.forward, 0.018f, new Color(0.45f, 1f, 0.5f));
            BuildPubWing(root, woodDark, brass, warm);

            // ---- Back bar: cabinet, mirror, three shelves of bottles with a warm glow under each
            var interior = Child(root, "Interior");
            Box(interior, "BackBar_Cabinet", -19.9f, -10.1f, F, F + 0.95f, -23.4f, -22.85f, woodDark);
            Box(interior, "BackBar_Top", -19.95f, -10.05f, F + 0.95f, F + 0.99f, -23.4f, -22.8f, woodMid);
            Box(interior, "BackBar_Mirror", -19.6f, -10.4f, F + 1.05f, F + 2.45f, -23.4f, -23.37f, mirror, false);
            Box(interior, "BackBar_SideW", -19.9f, -19.7f, F + 0.99f, F + 2.6f, -23.4f, -22.95f, woodDark);
            Box(interior, "BackBar_SideE", -10.3f, -10.1f, F + 0.99f, F + 2.6f, -23.4f, -22.95f, woodDark);
            Box(interior, "BackBar_Crown", -20f, -10f, F + 2.6f, F + 2.75f, -23.4f, -22.9f, woodDark, false);
            Material[] bottleMats = { bottleGreen, bottleAmber, bottleClear, bottleRed, bottleAmber, bottleGreen };
            int b = 0;
            foreach (float y in new[] { F + 1.15f, F + 1.6f, F + 2.05f })
            {
                Box(interior, "Shelf", -19.7f, -10.3f, y - 0.03f, y, -23.37f, -23.02f, woodMid, false);
                Box(interior, "Shelf_Glow", -19.6f, -10.4f, y - 0.034f, y - 0.03f, -23.1f, -23.05f, warmGlow, false);
                for (float x = -19.45f; x <= -10.5f; x += 0.42f)
                {
                    Material m = bottleMats[b % bottleMats.Length];
                    float h = 0.22f + (b * 37 % 7) * 0.015f;
                    float zz = -23.2f + (b % 2) * 0.06f;
                    Cyl(interior, "Bottle", x, zz, y, h, 0.075f, m);
                    Cyl(interior, "BottleNeck", x, zz, y + h, 0.08f, 0.028f, m);
                    if (b % 3 == 0)
                        Cyl(interior, "BottleLabel", x, zz, y + h * 0.3f, h * 0.35f, 0.078f, paper);
                    b++;
                }
            }

            // ---- The long bar (front panel + bartender = "E · 주문하기"; the top is a plain surface to put things on)
            Box(interior, "Bar_Top", -20.6f, -9.4f, F + 1.05f, F + 1.11f, -20.45f, -19.6f, woodDark);
            Box(interior, "Bar_ReturnW", -20.5f, -19.9f, F, F + 1.05f, -23.4f, -20.45f, woodMid);
            Box(interior, "Bar_ReturnW_Top", -20.6f, -19.85f, F + 1.05f, F + 1.11f, -23.4f, -20.45f, woodDark);
            Box(interior, "Bar_ReturnE", -10.1f, -9.5f, F, F + 1.05f, -23.4f, -20.45f, woodMid);
            Box(interior, "Bar_ReturnE_Top", -10.15f, -9.4f, F + 1.05f, F + 1.11f, -23.4f, -20.45f, woodDark);
            Prim(interior, PrimitiveType.Cylinder, "Bar_FootRail", new Vector3(-15f, F + 0.2f, -19.42f), new Vector3(0.05f, 5.5f, 0.05f), brass, false, new Vector3(0f, 0f, 90f));
            for (float x = -20f; x <= -10f; x += 2.5f)
                Box(interior, "Bar_RailBracket", x - 0.02f, x + 0.02f, F + 0.18f, F + 0.22f, -19.6f, -19.42f, brass, false);
            Box(interior, "Bar_Coaster", -13.25f, -12.75f, F + 1.11f, F + 1.115f, -20.25f, -19.85f, leather, false);
            var pickupPoint = At(interior, "PickupPoint", new Vector3(-13f, F + 1.115f, -20.05f));
            var pickupArea = At(interior, "PickupArea", new Vector3(-13f, F + 1.26f, -20.05f));
            pickupArea.localScale = new Vector3(0.9f, 0.3f, 0.7f);
            // Taps + register on the bar.
            Cyl(interior, "Tap_Tower", -11.6f, -20.25f, F + 1.11f, 0.45f, 0.09f, brass);
            Prim(interior, PrimitiveType.Cylinder, "Tap_Bar", new Vector3(-11.6f, F + 1.5f, -20.25f), new Vector3(0.05f, 0.3f, 0.05f), brass, false, new Vector3(0f, 0f, 90f));
            for (int i = 0; i < 3; i++)
                Cyl(interior, "Tap_Handle", -11.85f + i * 0.25f, -20.2f, F + 1.52f, 0.2f, 0.035f, i == 1 ? leather : woodDark);
            Box(interior, "Register", -17.9f, -17.5f, F + 1.11f, F + 1.33f, -20.4f, -20.1f, brass);
            Box(interior, "Glass_Rack", -16.9f, -16.3f, F + 1.11f, F + 1.13f, -20.4f, -20.15f, dark, false);
            for (int i = 0; i < 4; i++)
                Cyl(interior, "Rack_Glass", -16.82f + i * 0.15f, -20.27f, F + 1.13f, 0.1f, 0.07f, glass);

            var bar = At(root, "Bar", new Vector3(-15f, F, -20.1f));
            Prim(bar, PrimitiveType.Cube, "Bar_Front", new Vector3(0f, 0.525f, 0f), new Vector3(11f, 1.05f, 0.6f), woodMid, true);
            for (float x = -5f; x <= 5.01f; x += 1.25f)
                Prim(bar, PrimitiveType.Cube, "Bar_Trim", new Vector3(x, 0.525f, -0.302f), new Vector3(0.06f, 1.0f, 0.01f), woodDark);
            Prim(bar, PrimitiveType.Cube, "Bar_Kick", new Vector3(0f, 0.05f, -0.305f), new Vector3(11f, 0.1f, 0.012f), woodDark);
            CounterOutline(bar, new Vector3(0f, 0.53f, 0f), new Vector3(11.06f, 1.08f, 0.66f));
            var owner = Npc(bar, "Bartender", new Vector3(-15f, F, -21.35f), 0f, Lit("MAT_Pub_Vest", 0.16f, 0.12f, 0.1f, 0.3f), shirtWhite, dark, skin);
            Prim(owner.Root, PrimitiveType.Cube, "Collar", new Vector3(0f, 1.46f, 0.02f), new Vector3(0.24f, 0.06f, 0.24f), shirtWhite);
            Prim(owner.Root, PrimitiveType.Cube, "Apron", new Vector3(0f, 0.85f, 0.135f), new Vector3(0.42f, 0.55f, 0.012f), leather);
            Prim(owner.Root, PrimitiveType.Cube, "Moustache", new Vector3(0f, 1.63f, 0.122f), new Vector3(0.1f, 0.025f, 0.012f), dark);
            Prim(owner.Root, PrimitiveType.Cube, "Hair", new Vector3(0f, 1.81f, -0.01f), new Vector3(0.23f, 0.04f, 0.25f), dark);
            // Glass in the left hand, rag in the right; the right hand circles over the glass.
            var heldGlass = Prim(owner.ArmL, PrimitiveType.Cylinder, "PolishedGlass", new Vector3(0f, -0.66f, 0.02f), new Vector3(0.075f, 0.05f, 0.075f), glass);
            Prim(owner.ArmR, PrimitiveType.Cube, "Rag", new Vector3(0f, -0.64f, 0.02f), new Vector3(0.09f, 0.03f, 0.11f), Lit("MAT_Pub_Rag", 0.85f, 0.83f, 0.78f, 0.1f));
            var polish = owner.Root.gameObject.AddComponent<IdleLoopMotion>();
            Set(polish, "pivot", owner.ArmR);
            Set(polish, "baseEuler", new Vector3(-68f, -28f, 0f));
            Set(polish, "amplitude", new Vector3(7f, 0f, 10f));
            Set(polish, "cyclesPerSecond", 1.2f);
            var hold = owner.Root.gameObject.AddComponent<IdleLoopMotion>();
            Set(hold, "pivot", owner.ArmL);
            Set(hold, "baseEuler", new Vector3(-62f, 26f, 0f));
            Set(hold, "amplitude", new Vector3(2f, 0f, 2f));
            Set(hold, "cyclesPerSecond", 0.4f);
            Set(hold, "spinProp", heldGlass.transform);
            Set(hold, "spinSpeed", 70f);
            owner.ArmR.localRotation = Quaternion.Euler(-68f, -28f, 0f); // the pose shows outside Play too
            owner.ArmL.localRotation = Quaternion.Euler(-62f, 26f, 0f);
            var bubble = Text(owner.Root, "Bubble", "", owner.Root.position + Vector3.up * 2.15f, Vector3.forward, 0.012f, new Color(1f, 0.95f, 0.6f));
            var area = At(root, "PubArea", new Vector3(-15f, F + 1.7f, -18.35f));
            area.localScale = new Vector3(17.8f, 3.6f, 10.3f);
            var react = owner.Root.gameObject.AddComponent<DisturbanceReaction>();
            Set(react, "area", area);
            Set(react, "speakerName", "주인장");
            Set(react, "lines", new[] { "헤이, 뭐하는 거야!", "이봐, 조심 좀 하라고!" });
            Set(react, "bubble", bubble);
            Set(react, "body", owner.Root);

            // Chalkboard menu on the west wall (rewritten from the menu by the bar).
            Box(interior, "Chalkboard_Frame", -23.8f, -23.75f, F + 1.15f, F + 2.85f, -17.2f, -15.4f, woodDark, false);
            Box(interior, "Chalkboard", -23.75f, -23.73f, F + 1.2f, F + 2.8f, -17.15f, -15.45f, chalk, false);
            var menuText = Text(interior, "Chalkboard_Text", "OLD BARREL", new Vector3(-23.72f, F + 2.74f, -16.3f), Vector3.right, 0.0095f,
                new Color(0.96f, 0.94f, 0.88f), TextAnchor.UpperCenter);

            var menu = Child(root, "MenuItems");
            Vector3 at = pickupPoint.position + Vector3.up * 0.02f;
            MenuLine(menu, "DraftBeer", "맥주 500cc", 550, "술", at);
            MenuLine(menu, "WhiskyRocks", "위스키 온더락", 900, "술", at);
            MenuLine(menu, "Cocktail", "칵테일", 800, "술", at);
            MenuLine(menu, "FriedChicken", "프라이드 치킨", 1600, "안주", at);
            MenuLine(menu, "YangnyeomChicken", "양념 치킨", 1700, "안주", at);
            MenuLine(menu, "NachosSalsa", "나쵸 & 살사", 800, "안주", at);
            MenuLine(menu, "FishAndChips", "피시앤칩스", 1400, "안주", at);
            MenuLine(menu, "SausageFries", "소시지구이 & 감자튀김", 1200, "안주", at);

            var counter = bar.gameObject.AddComponent<BaristaCounter>();
            Configure(counter, owner.Look, menu, pickupPoint, pickupArea, menuText, "OLD BARREL  MENU", 2.5f,
                bar.Find("Outline_Counter"), owner.OutlineGroup, "주인장",
                "어서 와. 뭐로 줄까?", "{0}? 뭐로 줄까?", "그래, 천천히 봐.",
                "금방 줄게. 바에 올려둘게.", "돈이 모자란데?", "바에 있는 것부터 가져가.",
                "지금 만들고 있어. 잠깐만.", "주문한 거 나왔어. 바에서 가져가.");

            // ---- Night-only business: lights, bartender, counter, doors, sign (PubBusinessHours)
            var interiorArea = At(root, "PubInteriorArea", new Vector3(-18.7f, F + 1.7f, -18.7f));
            interiorArea.localScale = new Vector3(25.4f, 3.6f, 11.2f);
            var hours = root.gameObject.AddComponent<PubBusinessHours>();
            Set(hours, "frontDoor", frontDoor);
            Set(hours, "rearDoor", rearDoor);
            Set(hours, "interiorArea", interiorArea);
            Set(hours, "staff", owner.Root.gameObject);
            Set(hours, "counter", counter);
            Set(hours, "openSign", openSign);
            Set(hours, "lightRoot", root);
            Set(hours, "glowMaterials", new Object[] { warmGlow, Glow("MAT_WC_Tube", 0.93f, 0.97f, 1f), Glow("MAT_Pub_StaffBulb", 1f, 0.9f, 0.72f) });
            Set(hours, "glowOffMaterial", Existing("MAT_Light_Off"));

            // ---- Seating: 7 bar stools + four round tables (10 chairs) = 17 seats
            var seating = Child(root, "Seating");
            int s = 0;
            foreach (float x in new[] { -19.3f, -17.9f, -16.5f, -15.1f, -13.7f, -12.3f, -10.9f })
                BarStool(seating, "BarStool_" + (++s), new Vector3(x, F, -19.05f), 180f, leather, brass);
            int t = 0;
            void RoundTable(float x, float z, params float[] chairAngles)
            {
                t++;
                var g = Child(seating, "Table_" + t);
                Cyl(g, "Top", x, z, F + 0.72f, 0.04f, 0.8f, woodDark, true);
                Cyl(g, "Pedestal", x, z, F, 0.72f, 0.1f, woodDark, true);
                Cyl(g, "Base", x, z, F, 0.03f, 0.5f, brass);
                Cyl(g, "Candle", x, z, F + 0.76f, 0.08f, 0.07f, warmGlow);
                PointLight(g, "CandleLight", new Vector3(x, F + 0.95f, z), warm, 0.6f, 2.2f);
                int c = 0;
                foreach (float a in chairAngles)
                {
                    float rad = a * Mathf.Deg2Rad;
                    var pos = new Vector3(x + Mathf.Cos(rad) * 0.72f, F, z + Mathf.Sin(rad) * 0.72f);
                    Vector3 toTable = new Vector3(x - pos.x, 0f, z - pos.z);
                    float yaw = Quaternion.LookRotation(toTable, Vector3.up).eulerAngles.y;
                    PubChair(g, "Chair_" + (++c), pos, yaw, woodMid, leather);
                }
            }
            RoundTable(-21.4f, -16.3f, 90f, 210f, 330f);
            RoundTable(-18.3f, -15.0f, 0f, 180f);
            RoundTable(-10.7f, -15.0f, 0f, 180f);
            RoundTable(-8.2f, -17.4f, 30f, 150f, 270f);

            // ---- Decor
            var decor = Child(root, "Decor");
            foreach (var p in new[] { new Vector2(-7.0f, -14.2f), new Vector2(-6.95f, -15.05f) })
            {
                Cyl(decor, "Barrel", p.x, p.y, F, 0.9f, 0.6f, woodMid, true);
                Cyl(decor, "Barrel_Hoop", p.x, p.y, F + 0.15f, 0.04f, 0.61f, dark);
                Cyl(decor, "Barrel_Hoop", p.x, p.y, F + 0.7f, 0.04f, 0.61f, dark);
            }
            Prim(decor, PrimitiveType.Cylinder, "Dartboard", new Vector3(-6.22f, F + 1.75f, -18.6f), new Vector3(0.45f, 0.012f, 0.45f), dark, false, new Vector3(0f, 0f, 90f));
            Prim(decor, PrimitiveType.Cylinder, "Dartboard_Ring", new Vector3(-6.235f, F + 1.75f, -18.6f), new Vector3(0.3f, 0.012f, 0.3f), leather, false, new Vector3(0f, 0f, 90f));
            Prim(decor, PrimitiveType.Cylinder, "Dartboard_Bull", new Vector3(-6.25f, F + 1.75f, -18.6f), new Vector3(0.06f, 0.012f, 0.06f), Existing("MAT_Food_Lettuce"), false, new Vector3(0f, 0f, 90f));
            foreach (var p in new[] { -14.25f })
            {
                Box(decor, "Picture_Frame", -23.79f, -23.75f, F + 1.5f, F + 2.1f, p - 0.35f, p + 0.35f, woodDark, false);
                Box(decor, "Picture", -23.75f, -23.74f, F + 1.56f, F + 2.04f, p - 0.29f, p + 0.29f, p < -14f ? bottleAmber : bottleGreen, false);
            }

            // ---- Lighting: warm, low, the bar is the bright spot
            var lights = Child(root, "Lighting");
            foreach (float x in new[] { -18.2f, -15.0f, -11.8f })
            {
                Box(lights, "Pendant_Cord", x - 0.01f, x + 0.01f, F + 2.55f, C, -20.03f, -20.01f, dark, false);
                Cyl(lights, "Pendant_Shade", x, -20.02f, F + 2.37f, 0.18f, 0.32f, brass);
                Prim(lights, PrimitiveType.Sphere, "Pendant_Bulb", new Vector3(x, F + 2.35f, -20.02f), new Vector3(0.1f, 0.1f, 0.1f), warmGlow);
                PointLight(lights, "BarLight", new Vector3(x, F + 2.2f, -20.02f), warm, 2.2f, 4.5f);
            }
            PointLight(lights, "FillLight_W", new Vector3(-19.5f, C - 0.5f, -16.0f), new Color(1f, 0.8f, 0.6f), 1.9f, 9f);
            PointLight(lights, "FillLight_E", new Vector3(-10.5f, C - 0.5f, -16.0f), new Color(1f, 0.8f, 0.6f), 1.9f, 9f);
            PointLight(lights, "FillLight_Door", new Vector3(-15f, C - 0.5f, -14.6f), new Color(1f, 0.8f, 0.6f), 1.2f, 7f);
            PointLight(lights, "BackBarLight", new Vector3(-15f, F + 2.1f, -22.6f), new Color(1f, 0.7f, 0.4f), 1.2f, 5f);
            foreach (var p in new[] { new Vector3(-23.7f, F + 2.0f, -14.6f), new Vector3(-6.3f, F + 2.0f, -15.2f), new Vector3(-23.7f, F + 2.0f, -20.25f) })
            {
                Box(lights, "Sconce", p.x - 0.06f, p.x + 0.06f, p.y - 0.1f, p.y + 0.1f, p.z - 0.08f, p.z + 0.08f, warmGlow, false);
                PointLight(lights, "SconceLight", p + (p.x < -15f ? Vector3.right : Vector3.left) * 0.25f, warm, 0.7f, 3.2f);
            }

            // ---- Outside: hanging sign + lantern
            var outside = Child(root, "Exterior");
            Box(outside, "Sign_Board", -16.6f, -12.4f, F + 2.45f, F + 3.15f, -13.02f, -12.92f, woodDark, false);
            Box(outside, "Sign_Trim", -16.65f, -12.35f, F + 2.42f, F + 2.45f, -13.03f, -12.91f, brass, false);
            var pubSign = Text(outside, "Sign_Text", "OLD BARREL  PUB", new Vector3(-14.5f, F + 2.8f, -12.9f), Vector3.forward, 0.045f, new Color(1f, 0.85f, 0.55f));
            Set(root.GetComponent<PubBusinessHours>(), "litTexts", new Object[] { pubSign, menuText });
            Box(outside, "Lantern", -13.4f, -13.25f, F + 2.0f, F + 2.25f, -13.0f, -12.88f, warmGlow, false);
            PointLight(outside, "EntranceLight", new Vector3(-14.5f, F + 2.3f, -12.5f), warm, 1.2f, 4.5f);
            Box(outside, "Awning", -16.0f, -13.0f, F + 2.36f, F + 2.42f, -13.1f, -12.2f, woodDark, false);

            BuildPubSurroundings(root, woodDark, brass, dark, warm);
        }
    
        // =============================================================================================================
        // Pub surroundings: service yard (west strip x -35.4..-31.4) and a small parking lot (east strip x 5.2..10.2).
        // Blockout props only - colliders on the solid ones (future chase / alley use), nothing interactive.
        // =============================================================================================================

        private static void BuildPubSurroundings(Transform root, Material woodDark, Material brass, Material dark, Color warm)
        {
            float F = G;
            Material concrete = Existing("Blockout_Sidewalk");
            Material asphalt = Existing("Blockout_Parking");
            Material paint = Lit("MAT_Street_LinePaint", 0.93f, 0.93f, 0.88f, 0.2f);
            Material dumpster = Lit("MAT_Ext_Dumpster", 0.16f, 0.32f, 0.2f, 0.35f, 0.2f);
            Material bag = Lit("MAT_Ext_TrashBag", 0.05f, 0.05f, 0.06f, 0.7f);
            Material crate = Lit("MAT_Ext_BeerCrate", 0.85f, 0.55f, 0.1f, 0.3f);
            Material metal = Existing("MAT_Metal_Dull");
            Material ac = Lit("MAT_Ext_ACUnit", 0.78f, 0.78f, 0.76f, 0.35f);
            Material carBody = Existing("District_Car") ?? Lit("MAT_Ext_Car", 0.25f, 0.35f, 0.55f, 0.6f, 0.3f);
            Material glassDark = Existing("MAT_Window_Dark");
            Material tire = Existing("MAT_Prop_DarkPlastic");
            Material warmGlow = Existing("MAT_Pub_WarmGlow");
            Material yellow = Existing("MAT_FF_Yellow");

            // ---- West: service / bins yard
            var yard = Child(root, "ServiceYard");
            Box(yard, "Yard_Pad", -35.4f, -31.4f, F, F + 0.015f, -24.3f, -13.1f, concrete, false);
            Box(yard, "Dumpster", -34.9f, -33.1f, F + 0.15f, F + 1.15f, -23.9f, -22.7f, dumpster);
            Prim(yard, PrimitiveType.Cube, "Dumpster_Lid", new Vector3(-34.0f, F + 1.2f, -23.25f), new Vector3(1.85f, 0.06f, 1.25f), dumpster, false, new Vector3(-8f, 0f, 0f));
            foreach (var x in new[] { -34.75f, -33.25f })
                foreach (var z in new[] { -23.8f, -22.8f })
                    Prim(yard, PrimitiveType.Cylinder, "Dumpster_Wheel", new Vector3(x, F + 0.075f, z), new Vector3(0.15f, 0.03f, 0.15f), tire, false, new Vector3(0f, 0f, 90f));
            Prim(yard, PrimitiveType.Sphere, "TrashBag", new Vector3(-32.7f, F + 0.25f, -22.4f), new Vector3(0.6f, 0.5f, 0.55f), bag, true);
            Prim(yard, PrimitiveType.Sphere, "TrashBag", new Vector3(-32.2f, F + 0.2f, -22.9f), new Vector3(0.5f, 0.4f, 0.5f), bag, true);
            Prim(yard, PrimitiveType.Sphere, "TrashBag", new Vector3(-32.5f, F + 0.55f, -22.7f), new Vector3(0.45f, 0.38f, 0.45f), bag, false);
            for (int i = 0; i < 4; i++) // empty beer crates, stacked
                Box(yard, "BeerCrate", -35.2f, -34.7f, F + i * 0.3f, F + i * 0.3f + 0.28f, -19.9f + (i % 2) * 0.03f, -19.5f + (i % 2) * 0.03f, crate);
            Box(yard, "BeerCrate", -35.2f, -34.7f, F, F + 0.28f, -19.35f, -18.95f, crate);
            Cyl(yard, "Keg", -32.1f, -19.4f, F, 0.6f, 0.4f, metal, true);
            Cyl(yard, "Keg", -32.1f, -18.85f, F, 0.6f, 0.4f, metal, true);
            Cyl(yard, "Keg_Lying", -33.0f, -17.9f, F, 0.4f, 0.4f, metal, true);
            Box(yard, "ACUnit", -32.3f, -31.4f, F, F + 0.75f, -16.6f, -15.6f, ac);
            Prim(yard, PrimitiveType.Cylinder, "ACUnit_Fan", new Vector3(-32.31f, F + 0.38f, -16.1f), new Vector3(0.55f, 0.01f, 0.55f), dark, false, new Vector3(0f, 0f, 90f));
            Box(yard, "ACUnit_High", -31.9f, -31.4f, F + 2.6f, F + 3.2f, -21.0f, -20.2f, ac);
            Cyl(yard, "Pipe_Down", -31.48f, -16.9f, F, 6.9f, 0.12f, metal);
            Prim(yard, PrimitiveType.Cylinder, "Pipe_Across", new Vector3(-31.48f, F + 3.6f, -19.0f), new Vector3(0.1f, 2.1f, 0.1f), metal, false, new Vector3(90f, 0f, 0f));
            // The rear door itself is Doors/Door_Rear_Hinge (real, opens into the backroom); sign + lamp above it.
            Box(yard, "BackDoor_Frame", -31.48f, -31.4f, F + 2.2f, F + 2.3f, -22.55f, -20.85f, dark, false);
            Box(yard, "BackDoor_Sign", -31.45f, -31.43f, F + 2.32f, F + 2.52f, -22.0f, -21.4f, yellow, false);
            Text(yard, "BackDoor_Text", "STAFF", new Vector3(-31.47f, F + 2.42f, -21.7f), Vector3.left, 0.012f, Color.black);
            Box(yard, "BackDoor_Lamp", -31.55f, -31.4f, F + 2.6f, F + 2.75f, -21.8f, -21.6f, warmGlow, false);
            PointLight(yard, "BackDoorLight", new Vector3(-31.9f, F + 2.3f, -21.7f), warm, 0.9f, 5f);

            // ---- East: small customer parking (2 bays, one car parked)
            var lot = Child(root, "Parking");
            Box(lot, "Lot_Asphalt", 5.2f, 10.2f, F, F + 0.015f, -24.3f, -13.1f, asphalt, false);
            Box(lot, "Lot_Curb", 5.2f, 10.2f, F, F + 0.15f, -24.3f, -24.0f, concrete);
            foreach (var x in new[] { 5.35f, 7.7f, 10.05f })
                Box(lot, "Bay_Line", x - 0.06f, x + 0.06f, F + 0.015f, F + 0.02f, -24.0f, -18.6f, paint, false);
            Box(lot, "Bay_Front", 5.35f, 10.05f, F + 0.015f, F + 0.02f, -18.66f, -18.54f, paint, false);
            foreach (var x in new[] { 6.52f, 8.88f })
                Box(lot, "WheelStop", x - 0.8f, x + 0.8f, F, F + 0.12f, -23.55f, -23.35f, concrete);
            // Parked car in bay 1 (nose to the curb).
            var car = Child(lot, "ParkedCar");
            Box(car, "Car_Body", 5.7f, 7.35f, F + 0.3f, F + 0.95f, -23.3f, -19.0f, carBody);
            Box(car, "Car_Cabin", 5.8f, 7.25f, F + 0.95f, F + 1.45f, -22.4f, -19.9f, carBody);
            Box(car, "Car_Windows", 5.78f, 7.27f, F + 1.0f, F + 1.38f, -22.3f, -20.0f, glassDark, false);
            foreach (var x in new[] { 5.72f, 7.33f })
                foreach (var z in new[] { -22.6f, -19.7f })
                    Prim(car, PrimitiveType.Cylinder, "Car_Wheel", new Vector3(x, F + 0.32f, z), new Vector3(0.64f, 0.1f, 0.64f), tire, false, new Vector3(0f, 0f, 90f));
            // Bollards keep cars off the pub's front wall; a sign + bin at the lot mouth.
            for (int i = 0; i < 3; i++)
            {
                Cyl(lot, "Bollard", 5.5f, -14.0f - i * 1.6f, F, 0.9f, 0.16f, metal, true);
                Cyl(lot, "Bollard_Band", 5.5f, -14.0f - i * 1.6f, F + 0.7f, 0.08f, 0.165f, yellow);
            }
            Cyl(lot, "Sign_Post", 9.8f, -13.6f, F, 2.2f, 0.06f, metal, true);
            Box(lot, "Sign_Plate", 9.45f, 10.15f, F + 1.7f, F + 2.2f, -13.62f, -13.58f, Lit("MAT_Ext_ParkingBlue", 0.15f, 0.3f, 0.65f, 0.3f), false);
            Text(lot, "Sign_Text", "P\nPUB", new Vector3(9.8f, F + 1.95f, -13.56f), Vector3.forward, 0.012f, Color.white);
            Cyl(lot, "TrashBin", 9.7f, -14.6f, F, 0.9f, 0.5f, Existing("MAT_Bin_Municipal"), true);
            Box(lot, "WallLamp", 5.2f, 5.35f, F + 2.8f, F + 2.95f, -19.0f, -18.8f, warmGlow, false);
            PointLight(lot, "LotLight", new Vector3(5.8f, F + 2.8f, -19.0f), warm, 1.0f, 7f);
        }
            // =============================================================================================================
        // Pub west wing (inside the block's former west filler, x -31.4..-24): restroom (north) + staff backroom (south).
        // Restroom after DesignReferences/wc_1 (partition stalls on metal posts, cream tiles, dark green upper wall,
        // checker floor, green door) and wc_2 (stone counter, square basins, framed mirror, box soap dispensers,
        // silver hand dryer). Backroom: Hall → STAFF ONLY door → backroom → rear door → ServiceYard.
        // =============================================================================================================

        private static void BuildPubWing(Transform root, Material woodDark, Material brass, Color warm)
        {
            float F = G, C = -0.1f;
            float x0 = -31.2f, x1 = -24.0f;
            Material outer = Existing("District_BuildingFuture");
            var wing = Child(root, "Wing");

            // ---- Shell (Pub_* → part of the carve bounds)
            Box(wing, "Pub_Wing_WallN", -31.4f, -24.0f, F, C, -13.3f, -13.1f, outer);
            Box(wing, "Pub_Wing_WallS", -31.4f, -24.0f, F, C, -24.3f, -24.1f, outer);
            Box(wing, "Pub_Wing_WallW_A", -31.4f, -31.2f, F, C, -24.1f, -22.5f, outer);
            Box(wing, "Pub_Wing_WallW_Lintel", -31.4f, -31.2f, F + 2.2f, C, -22.5f, -20.9f, outer);
            Box(wing, "Pub_Wing_WallW_B", -31.4f, -31.2f, F, C, -20.9f, -13.3f, outer);
            Box(wing, "Pub_Wing_Divider", x0, x1, F, C, -19.6f, -19.4f, outer);
            Box(wing, "Pub_Wing_Ceiling", -31.4f, -24.0f, C, C + 0.1f, -24.3f, -13.1f, Existing("MAT_Pub_Ceiling"), true);

            // ---- Restroom (x -31.2..-24.0, z -19.4..-13.3 = 7.2 × 6.1 m)
            var wc = Child(wing, "Restroom");
            Material tile = Lit("MAT_WC_Tile", 0.88f, 0.85f, 0.74f, 0.55f);
            Material upper = Lit("MAT_WC_UpperWall", 0.24f, 0.33f, 0.28f, 0.25f);
            Material floorLight = Lit("MAT_WC_FloorLight", 0.8f, 0.76f, 0.68f, 0.45f);
            Material floorDark = Lit("MAT_WC_FloorDark", 0.4f, 0.14f, 0.11f, 0.45f);
            Material partition = Lit("MAT_WC_Partition", 0.8f, 0.8f, 0.77f, 0.4f);
            Material chrome = Lit("MAT_WC_Chrome", 0.85f, 0.87f, 0.9f, 0.9f, 0.9f);
            Material porcelain = Lit("MAT_WC_Porcelain", 0.96f, 0.96f, 0.95f, 0.8f);
            Material stone = Lit("MAT_WC_Stone", 0.8f, 0.78f, 0.74f, 0.5f);
            Material basinInner = Lit("MAT_WC_BasinInner", 0.72f, 0.74f, 0.76f, 0.9f);
            Material mirrorMat = Lit("MAT_WC_Mirror", 0.62f, 0.68f, 0.72f, 0.97f, 0.6f);
            Material greenDoor = Lit("MAT_WC_Door", 0.14f, 0.3f, 0.22f, 0.35f);
            Material soap = Lit("MAT_WC_Soap", 0.3f, 0.3f, 0.32f, 0.6f);
            Material frame = Existing("MAT_Prop_DarkPlastic");
            Material tube = Glow("MAT_WC_Tube", 0.93f, 0.97f, 1f);
            Material paper = Existing("MAT_Cafe_CupPaper");
            float z0 = -19.4f, z1 = -13.3f;
            Box(wc, "WC_Floor", x0, x1, F, F + 0.01f, z0, z1, floorLight, false);
            for (int ix = 0; x0 + ix * 0.6f < x1 - 0.01f; ix++)
                for (int iz = 0; z0 + iz * 0.6f < z1 - 0.01f; iz++)
                    if ((ix + iz) % 2 == 0)
                        Box(wc, "WC_FloorTile", x0 + ix * 0.6f, Mathf.Min(x0 + ix * 0.6f + 0.6f, x1), F + 0.01f, F + 0.012f,
                            z0 + iz * 0.6f, Mathf.Min(z0 + iz * 0.6f + 0.6f, z1), floorDark, false);
            Box(wc, "WC_Ceiling", x0, x1, C - 0.02f, C, z0, z1, Lit("MAT_WC_Ceiling", 0.85f, 0.85f, 0.82f, 0.2f), false);
            void Lining(string n, float ax0, float ax1, float az0, float az1)
            {
                Box(wc, "Lining_" + n + "_Tile", ax0, ax1, F, F + 1.3f, az0, az1, tile, false);
                Box(wc, "Lining_" + n + "_Paint", ax0, ax1, F + 1.3f, C - 0.02f, az0, az1, upper, false);
            }
            Lining("W", x0, x0 + 0.02f, z0, z1);
            Lining("N", x0, x1, z1 - 0.02f, z1);
            Lining("S", x0, x1, z0, z0 + 0.02f);
            Lining("E1", x1 - 0.02f, x1, z0, -19.2f);
            Lining("E2", x1 - 0.02f, x1, -17.6f, z1);
            Box(wc, "Lining_E_Lintel", x1 - 0.02f, x1, F + 2.2f, C - 0.02f, -19.2f, -17.6f, upper, false);

            // Three stalls along the north wall (1.25 × 1.88 m each), partitions raised 0.15 m like wc_1.
            const float sw = 1.25f, front = -15.2f;
            for (int i = 0; i < 3; i++)
            {
                float sx0 = x0 + i * sw, sx1 = sx0 + sw, cx = (sx0 + sx1) * 0.5f;
                var stall = Child(wc, "Stall_" + (i + 1));
                Box(stall, "Partition", sx1 - 0.02f, sx1 + 0.02f, F + 0.15f, F + 1.95f, front, z1 - 0.02f, partition);
                Box(stall, "Front_W", sx0 + 0.03f, cx - 0.43f, F + 0.15f, F + 1.95f, front - 0.02f, front + 0.02f, partition);
                Box(stall, "Front_E", cx + 0.43f, sx1 - 0.03f, F + 0.15f, F + 1.95f, front - 0.02f, front + 0.02f, partition);
                Box(stall, "Post", sx1 - 0.03f, sx1 + 0.03f, F, F + 2.0f, front - 0.03f, front + 0.03f, chrome);
                if (i == 0)
                    Box(stall, "Post", sx0 + 0.02f, sx0 + 0.08f, F, F + 2.0f, front - 0.03f, front + 0.03f, chrome);
                var door = Door(stall, "StallDoor_Hinge", new Vector3(cx + 0.425f, F, front), 90f, false, null,
                    openPrompt: "칸 문 열기", leafMat: partition, leafSize: new Vector3(0.84f, 1.75f, 0.15f));
                if (door != null)
                    Set(door, "closePrompt", "칸 문 닫기");
                // Toilet: one collider, separate object (later: a use interaction).
                var toilet = At(stall, "Toilet", new Vector3(cx, F, -13.75f));
                var tc = toilet.gameObject.AddComponent<BoxCollider>();
                tc.size = new Vector3(0.42f, 0.8f, 0.72f);
                tc.center = new Vector3(0f, 0.4f, 0f);
                Prim(toilet, PrimitiveType.Cylinder, "Base", new Vector3(0f, 0.19f, -0.05f), new Vector3(0.3f, 0.19f, 0.36f), porcelain);
                Prim(toilet, PrimitiveType.Cylinder, "Bowl", new Vector3(0f, 0.38f, -0.12f), new Vector3(0.4f, 0.04f, 0.48f), porcelain);
                Prim(toilet, PrimitiveType.Cube, "Tank", new Vector3(0f, 0.62f, 0.27f), new Vector3(0.4f, 0.4f, 0.16f), porcelain);
                Prim(toilet, PrimitiveType.Cube, "TankLid", new Vector3(0f, 0.83f, 0.27f), new Vector3(0.42f, 0.03f, 0.18f), porcelain);
                BathroomKit.UpgradeToilet(toilet, new BathroomKit.ToiletShape
                {
                    BowlTop = 0.42f, PivotZ = 0.15f, TankFrontZ = 0.19f, TankTop = 0.845f, TankZ = 0.27f, TankDepth = 0.16f
                }, porcelain, chrome, Existing("MAT_Water_Simple"));
                // Paper holder + roll on the stall's west side.
                Box(stall, "PaperHolder", sx0 + 0.03f, sx0 + 0.08f, F + 0.66f, F + 0.74f, -14.25f, -14.05f, chrome, false);
                Prim(stall, PrimitiveType.Cylinder, "PaperRoll", new Vector3(sx0 + 0.13f, F + 0.7f, -14.15f), new Vector3(0.11f, 0.05f, 0.11f), paper, false, new Vector3(0f, 0f, 90f));
            }
            Box(wc, "Stall_TopRail", x0, x0 + 3 * sw + 0.03f, F + 1.95f, F + 2.0f, front - 0.03f, front + 0.03f, chrome, false);

            // Sinks along the divider wall (facing north): stone counter, two square basins, one big framed mirror.
            Box(wc, "Sink_Counter", -31.0f, -28.0f, F + 0.75f, F + 0.85f, z0 + 0.02f, z0 + 0.6f, stone);
            foreach (float cx in new[] { -30.25f, -28.75f })
            {
                Box(wc, "Basin", cx - 0.24f, cx + 0.24f, F + 0.85f, F + 0.99f, z0 + 0.12f, z0 + 0.52f, porcelain, false);
                Box(wc, "Basin_Inner", cx - 0.19f, cx + 0.19f, F + 0.99f, F + 1.0f, z0 + 0.16f, z0 + 0.48f, basinInner, false);
                BathroomKit.Faucet(wc, "Tap", new Vector3(cx, F + 0.85f, z0 + 0.08f), Vector3.forward, 0.2f, 0.12f, F + 0.99f,
                    chrome, Existing("MAT_Water_Simple"));
            }
            Box(wc, "Mirror_Frame", -31.05f, -27.95f, F + 1.08f, F + 2.12f, z0 + 0.02f, z0 + 0.05f, frame, false);
            Box(wc, "Mirror", -30.98f, -28.02f, F + 1.12f, F + 2.08f, z0 + 0.05f, z0 + 0.06f, mirrorMat, false);
            Box(wc, "SoapDispenser", -29.56f, -29.44f, F + 0.9f, F + 1.06f, z0 + 0.02f, z0 + 0.1f, soap, false);
            Box(wc, "SoapDispenser", -31.12f, -31.0f, F + 0.9f, F + 1.06f, z0 + 0.15f, z0 + 0.27f, soap, false);
            Box(wc, "HandDryer", -27.6f, -27.25f, F + 1.15f, F + 1.45f, z0 + 0.02f, z0 + 0.22f, chrome, false);
            Box(wc, "HandDryer_Nozzle", -27.47f, -27.38f, F + 1.1f, F + 1.15f, z0 + 0.12f, z0 + 0.2f, frame, false);
            Cyl(wc, "TrashBin", -26.9f, z0 + 0.32f, F, 0.6f, 0.4f, Existing("MAT_Bin_Municipal"), true);
            Box(wc, "Utility_Cabinet", -24.65f, -24.05f, F, F + 1.7f, -14.3f, -13.35f, greenDoor);
            Cyl(wc, "MopBucket", -25.0f, -14.85f, F, 0.3f, 0.35f, Existing("MAT_FF_Yellow"), true);
            // Fluorescent tubes (neutral / slightly cool).
            var cool = new Color(0.92f, 0.97f, 1f);
            foreach (float x in new[] { -29.3f, -26.1f })
            {
                Box(wc, "Fluorescent", x - 0.9f, x + 0.9f, C - 0.07f, C - 0.02f, -16.45f, -16.25f, tube, false);
                PointLight(wc, "WCLight", new Vector3(x, C - 0.35f, -16.35f), cool, 1.5f, 6.5f);
            }

            // ---- Staff backroom (x -31.2..-24.0, z -24.1..-19.6 = 7.2 × 4.5 m); route along z ≈ -21.9 stays clear.
            var room = Child(wing, "Backroom");
            Material wall = Lit("MAT_Pub_BackroomWall", 0.62f, 0.58f, 0.5f, 0.15f);
            Material steel = Existing("MAT_Metal_Dull");
            Material carton = Existing("MAT_Pack_Pulp");
            Material crate = Lit("MAT_Ext_BeerCrate", 0.85f, 0.55f, 0.1f, 0.3f);
            Material locker = Lit("MAT_Locker", 0.38f, 0.47f, 0.58f, 0.45f);
            Material bulb = Glow("MAT_Pub_StaffBulb", 1f, 0.9f, 0.72f);
            Material glass = Existing("Mat_Glass_Clear");
            float rz0 = -24.1f, rz1 = -19.6f;
            Box(room, "Staff_Floor", x0, x1, F, F + 0.01f, rz0, rz1, Lit("MAT_Pub_StaffFloor", 0.45f, 0.44f, 0.42f, 0.3f), false);
            Box(room, "Staff_Ceiling", x0, x1, C - 0.02f, C, rz0, rz1, Existing("MAT_Pub_Ceiling"), false);
            Box(room, "Lining_W_A", x0, x0 + 0.02f, F, C - 0.02f, rz0, -22.5f, wall, false);
            Box(room, "Lining_W_B", x0, x0 + 0.02f, F, C - 0.02f, -20.9f, rz1, wall, false);
            Box(room, "Lining_W_Lintel", x0, x0 + 0.02f, F + 2.2f, C - 0.02f, -22.5f, -20.9f, wall, false);
            Box(room, "Lining_S", x0, x1, F, C - 0.02f, rz0, rz0 + 0.02f, wall, false);
            Box(room, "Lining_N", x0, x1, F, C - 0.02f, rz1 - 0.02f, rz1, wall, false);
            Box(room, "Lining_E_A", x1 - 0.02f, x1, F, C - 0.02f, rz0, -22.9f, wall, false);
            Box(room, "Lining_E_B", x1 - 0.02f, x1, F, C - 0.02f, -21.3f, rz1, wall, false);
            Box(room, "Lining_E_Lintel", x1 - 0.02f, x1, F + 2.2f, C - 0.02f, -22.9f, -21.3f, wall, false);
            var rnd = new System.Random(11);
            void ShelfUnit(float sx0, float sx1)
            {
                var u = Child(room, "ShelfUnit");
                var col = At(u, "Collider", new Vector3((sx0 + sx1) * 0.5f, F + 1.0f, -23.83f));
                col.gameObject.AddComponent<BoxCollider>().size = new Vector3(sx1 - sx0, 2.0f, 0.5f);
                foreach (float x in new[] { sx0 + 0.02f, sx1 - 0.02f })
                    foreach (float z in new[] { -24.06f, -23.6f })
                        Box(u, "Upright", x - 0.02f, x + 0.02f, F, F + 2.0f, z - 0.02f, z + 0.02f, steel, false);
                foreach (float y in new[] { 0.1f, 0.6f, 1.1f, 1.6f })
                {
                    Box(u, "Board", sx0, sx1, F + y, F + y + 0.03f, -24.06f, -23.6f, steel, false);
                    for (float x = sx0 + 0.08f; x < sx1 - 0.3f; x += 0.42f)
                    {
                        float w = 0.28f + (float)rnd.NextDouble() * 0.08f, h = 0.2f + (float)rnd.NextDouble() * 0.15f;
                        Material m = rnd.Next(3) == 0 ? crate : carton;
                        Box(u, "Box", x, x + w, F + y + 0.03f, F + y + 0.03f + h, -23.98f, -23.66f, m, false);
                    }
                }
            }
            ShelfUnit(-30.9f, -28.9f);
            ShelfUnit(-28.5f, -26.5f);
            Cyl(room, "Keg", -26.0f, -23.75f, F, 0.6f, 0.42f, steel, true);
            Cyl(room, "Keg", -25.45f, -23.75f, F, 0.6f, 0.42f, steel, true);
            for (int i = 0; i < 5; i++)
                Box(room, "BeerCrate", -24.9f, -24.35f, F + i * 0.3f, F + i * 0.3f + 0.28f, -24.05f, -23.65f, crate);
            // North side: lockers, work table with spare glasses, utility sink, cleaning corner.
            BuildLocker(room, "HideLocker_1", new Vector3(-30.75f, F, -19.995f), 180f, locker, frame);
            BuildLocker(room, "HideLocker_2", new Vector3(-29.9f, F, -19.995f), 180f, locker, frame);
            Box(room, "WorkTable_Top", -29.3f, -27.5f, F + 0.86f, F + 0.9f, -20.35f, -19.65f, woodDark);
            foreach (var p in new[] { new Vector2(-29.25f, -20.3f), new Vector2(-27.55f, -20.3f), new Vector2(-29.25f, -19.7f), new Vector2(-27.55f, -19.7f) })
                Box(room, "WorkTable_Leg", p.x - 0.03f, p.x + 0.03f, F, F + 0.86f, p.y - 0.03f, p.y + 0.03f, woodDark);
            Box(room, "GlassRack", -29.1f, -28.5f, F + 0.9f, F + 0.92f, -20.25f, -19.75f, frame, false);
            for (int i = 0; i < 6; i++)
                Cyl(room, "SpareGlass", -29.0f + (i % 3) * 0.2f, -20.1f + (i / 3) * 0.2f, F + 0.92f, 0.11f, 0.075f, glass);
            Box(room, "LiquorBox", -28.4f, -27.9f, F + 0.9f, F + 1.18f, -20.2f, -19.8f, carton, false);
            Box(room, "UtilitySink", -27.2f, -26.5f, F, F + 0.85f, -20.2f, -19.62f, steel);
            Box(room, "UtilitySink_Basin", -27.12f, -26.58f, F + 0.85f, F + 0.86f, -20.12f, -19.7f, basinInner, false);
            Cyl(room, "UtilitySink_Tap", -26.85f, -19.68f, F + 0.85f, 0.25f, 0.03f, chrome);
            Cyl(room, "MopBucket", -25.9f, -20.05f, F, 0.3f, 0.35f, Existing("MAT_FF_Yellow"), true);
            Prim(room, PrimitiveType.Cylinder, "MopHandle", new Vector3(-25.85f, F + 0.75f, -19.95f), new Vector3(0.03f, 0.6f, 0.03f), woodDark, false, new Vector3(8f, 0f, 0f));
            Box(room, "CleaningShelf", -25.4f, -24.6f, F + 1.2f, F + 1.23f, -19.85f, -19.62f, steel, false);
            for (int i = 0; i < 4; i++)
                Cyl(room, "SprayBottle", -25.3f + i * 0.2f, -19.74f, F + 1.23f, 0.22f, 0.07f, i % 2 == 0 ? Existing("MAT_FF_CupGreen") : Existing("MAT_Play_Blue"));
            Box(room, "ExitSign", x0 + 0.02f, x0 + 0.04f, F + 2.3f, F + 2.5f, -22.05f, -21.35f, Lit("MAT_ExitSign", 0.1f, 0.55f, 0.25f, 0.3f), false);
            Text(room, "ExitSign_Text", "SERVICE EXIT", new Vector3(x0 + 0.05f, F + 2.4f, -21.7f), Vector3.right, 0.007f, Color.white);
            var practical = new Color(1f, 0.9f, 0.75f);
            foreach (float x in new[] { -29.0f, -25.8f })
            {
                Prim(room, PrimitiveType.Sphere, "Bulb", new Vector3(x, C - 0.15f, -21.9f), new Vector3(0.12f, 0.12f, 0.12f), bulb);
                Box(room, "BulbCord", x - 0.008f, x + 0.008f, C - 0.1f, C - 0.02f, -21.91f, -21.89f, frame, false);
                PointLight(room, "StaffLight", new Vector3(x, C - 0.3f, -21.9f), practical, 1.4f, 5.5f);
            }
        }

        // =============================================================================================================
        // Fast Food back of house: kitchen behind the counter line (z -44.4..-40.2) + storage (z -40..-37.6) up to the
        // block's north face. Route: Hall → STAFF ONLY (west partition) → corridor x -30.8..-27 → storage → back door.
        // =============================================================================================================

        private static void BuildFastFoodBackOfHouse(Transform root, Material steel, Material dark, Material red, Material yellow, Material white)
        {
            float F = G, C = 0.1f;
            var boh = Child(root, "BackOfHouse");
            Material locker = Lit("MAT_Locker", 0.38f, 0.47f, 0.58f, 0.45f);
            Material carton = Existing("MAT_Pack_Pulp");
            Material panelGlow = Existing("MAT_FF_LightPanel");
            Box(boh, "TrashBin", -17.0f, -16.35f, F, F + 0.9f, -44.4f, -43.8f, dark);
            Box(boh, "TrashBin2", -17.75f, -17.1f, F, F + 0.9f, -44.4f, -43.8f, dark);

            // Employee changing room (the old "WALK-IN" spot): hideable lockers, bench, coat hooks, notice board, bin.
            var changing = Child(boh, "ChangingRoom");
            Box(changing, "Changing_Floor", -19.4f, -16.2f, F + 0.01f, F + 0.012f, -40.9f, -37.6f, Lit("MAT_FF_ChangingFloor", 0.55f, 0.6f, 0.66f, 0.3f), false);
            Box(changing, "Sign_Plate", -19.63f, -19.61f, F + 2.3f, F + 2.55f, -39.3f, -38.3f, red, false);
            Text(changing, "Sign_Text", "STAFF ROOM\n직원 탈의실", new Vector3(-19.64f, F + 2.425f, -38.8f), Vector3.left, 0.0085f, Color.white);
            BuildLocker(changing, "HideLocker_1", new Vector3(-18.9f, F, -40.525f), 0f, locker, dark);
            BuildLocker(changing, "HideLocker_2", new Vector3(-18.05f, F, -40.525f), 0f, locker, dark);
            Box(changing, "Bench_Top", -17.45f, -16.45f, F + 0.42f, F + 0.46f, -40.85f, -40.5f, Existing("MAT_Bench_Wood"));
            foreach (float x in new[] { -17.38f, -16.52f })
                Box(changing, "Bench_Leg", x - 0.03f, x + 0.03f, F, F + 0.42f, -40.8f, -40.55f, steel);
            for (int i = 0; i < 3; i++)
            {
                float x = -19.15f + i * 0.35f;
                Box(changing, "CoatHook", x - 0.015f, x + 0.015f, F + 1.6f, F + 1.63f, -37.66f, -37.6f, steel, false);
                if (i != 1)
                    Box(changing, "Jacket", x - 0.17f, x + 0.17f, F + 0.95f, F + 1.6f, -37.75f, -37.66f, i == 0 ? red : dark, false);
            }
            Box(changing, "NoticeBoard", -16.22f, -16.2f, F + 1.2f, F + 1.75f, -39.3f, -38.5f, Lit("MAT_Cork", 0.62f, 0.45f, 0.28f, 0.1f), false);
            for (int i = 0; i < 4; i++)
                Box(changing, "Notice", -16.23f, -16.22f, F + 1.3f + (i % 2) * 0.22f, F + 1.48f + (i % 2) * 0.22f,
                    -39.2f + (i / 2) * 0.36f, -38.95f + (i / 2) * 0.36f, white, false);
            Cyl(changing, "TrashBin", -19.15f, -37.85f, F, 0.4f, 0.3f, dark, true);
            PointLight(changing, "ChangingLight", new Vector3(-17.9f, C - 0.3f, -39.2f), new Color(1f, 0.99f, 0.96f), 1.6f, 5.5f);
            Box(changing, "ChangingPanel", -18.4f, -17.4f, C - 0.03f, C, -39.4f, -39.0f, panelGlow, false);
            var rnd = new System.Random(5);
            Material[] goods = { carton, Existing("MAT_Food_Bun"), red, yellow, Existing("MAT_Food_Fries") };
            foreach (var sx in new[] { (-28.4f, -26.4f), (-26.0f, -24.0f) })
            {
                var u = Child(boh, "IngredientShelf");
                var col = At(u, "Collider", new Vector3((sx.Item1 + sx.Item2) * 0.5f, F + 1.0f, -37.86f));
                col.gameObject.AddComponent<BoxCollider>().size = new Vector3(sx.Item2 - sx.Item1, 2.0f, 0.48f);
                foreach (float x in new[] { sx.Item1 + 0.02f, sx.Item2 - 0.02f })
                    Box(u, "Upright", x - 0.02f, x + 0.02f, F, F + 2.0f, -38.1f, -37.62f, steel, false);
                foreach (float y in new[] { 0.1f, 0.6f, 1.1f, 1.6f })
                {
                    Box(u, "Board", sx.Item1, sx.Item2, F + y, F + y + 0.03f, -38.1f, -37.62f, steel, false);
                    for (float x = sx.Item1 + 0.08f; x < sx.Item2 - 0.3f; x += 0.4f)
                        Box(u, "Stock", x, x + 0.3f, F + y + 0.03f, F + y + 0.23f + (float)rnd.NextDouble() * 0.12f, -38.02f, -37.7f,
                            goods[rnd.Next(goods.Length)], false);
                }
            }
            for (int i = 0; i < 3; i++)
                Box(boh, "DryStorage_Box", -23.4f, -22.5f, F + i * 0.42f, F + i * 0.42f + 0.4f, -38.2f, -37.7f, carton);
            Box(boh, "ChestFreezer", -22.45f, -21.25f, F, F + 0.9f, -38.4f, -37.65f, white);
            Box(boh, "ChestFreezer_Lid", -22.47f, -21.23f, F + 0.9f, F + 0.95f, -38.42f, -37.63f, steel, false);
            Box(boh, "MopSink", -27.6f, -26.9f, F, F + 0.5f, -39.98f, -39.5f, steel);
            Cyl(boh, "MopBucket", -26.4f, -39.7f, F, 0.3f, 0.35f, yellow, true);
            Box(boh, "BackDoor_ExitSign", -17.8f, -16.6f, F + 2.3f, F + 2.5f, -37.62f, -37.6f, Lit("MAT_ExitSign", 0.1f, 0.55f, 0.25f, 0.3f), false);
            Text(boh, "BackDoor_ExitText", "EXIT", new Vector3(-17.2f, F + 2.4f, -37.63f), Vector3.back, 0.012f, Color.white);

            // Bright neutral work light
            var work = new Color(1f, 0.99f, 0.96f);
            foreach (var p in new[] { new Vector2(-29f, -42.4f), new Vector2(-21.5f, -42.8f), new Vector2(-27f, -38.8f), new Vector2(-21f, -38.8f) })
            {
                PointLight(boh, "KitchenLight", new Vector3(p.x, C - 0.3f, p.y), work, 2.0f, 6f);
                Box(boh, "KitchenPanel", p.x - 0.5f, p.x + 0.5f, C - 0.03f, C, p.y - 0.2f, p.y + 0.2f, panelGlow, false);
            }

            // Outside the back door: a small service corner (bins, crates, grease drum, door lamp).
            var outside = Child(boh, "BackDoorExterior");
            // The back door opens from the changing room into the narrow alley between this block and the facade behind.
            Box(outside, "BackDoor_Frame", -18.1f, -16.3f, F + 2.2f, F + 2.3f, -37.4f, -37.32f, dark, false);
            Box(outside, "BackDoor_Sign", -17.5f, -16.9f, F + 2.32f, F + 2.52f, -37.4f, -37.38f, yellow, false);
            Text(outside, "BackDoor_Text", "STAFF", new Vector3(-17.2f, F + 2.42f, -37.37f), Vector3.forward, 0.012f, Color.black);
            Box(outside, "BackDoor_Lamp", -17.3f, -17.1f, F + 2.6f, F + 2.75f, -37.4f, -37.25f, Existing("MAT_Pub_WarmGlow"), false);
            PointLight(outside, "BackDoorLight", new Vector3(-17.2f, F + 2.5f, -36.9f), new Color(1f, 0.85f, 0.65f), 0.9f, 5f);
            Material bin = Existing("MAT_Ext_Dumpster");
            Box(outside, "WheelieBin", -15.6f, -14.96f, F, F + 1.05f, -37.35f, -36.7f, bin);
            Box(outside, "WheelieBin_Lid", -15.62f, -14.94f, F + 1.05f, F + 1.1f, -37.37f, -36.65f, dark, false);
            for (int i = 0; i < 2; i++)
                Box(outside, "Crate", -19.4f, -18.95f, F + i * 0.3f, F + i * 0.3f + 0.28f, -37.35f, -36.95f, red);
        }
            // =============================================================================================================
        // Hideable locker (pub backroom, fast-food changing room): 0.8 × 0.75 × 2.15 m cabinet, a SwingDoor leaf with a real
        // vent slit at eye height (the leaf's own mesh is hidden; panels around the slit ride the hinge), and a
        // HideableLocker on the root whose collider fills the inside (aimed only while the door is open).
        // Local frame: the door is on +Z (the locker faces +Z), hinged on the +X side.
        // =============================================================================================================

        private static void BuildLocker(Transform parent, string name, Vector3 floorPos, float yaw, Material body, Material dark)
        {
            const float W = 0.8f, D = 0.75f, H = 2.15f, T = 0.03f, Eye = 1.62f;
            var root = At(parent, name, floorPos, yaw);
            var inside = root.gameObject.AddComponent<BoxCollider>();
            inside.size = new Vector3(W - 2f * T - 0.02f, 1.9f, 0.6f);
            inside.center = new Vector3(0f, 1.07f, -0.02f);
            void Part(string n, Vector3 lp, Vector3 ls, Material m, bool col)
            {
                var go = Prim(root, PrimitiveType.Cube, n, lp, ls, m, col);
                go.transform.localRotation = Quaternion.identity;
            }
            Part("Back", new Vector3(0f, H * 0.5f, -D * 0.5f + T * 0.5f), new Vector3(W, H, T), body, true);
            Part("Side_L", new Vector3(-W * 0.5f + T * 0.5f, H * 0.5f, 0f), new Vector3(T, H, D), body, true);
            Part("Side_R", new Vector3(W * 0.5f - T * 0.5f, H * 0.5f, 0f), new Vector3(T, H, D), body, true);
            Part("Top", new Vector3(0f, H - T * 0.5f, 0f), new Vector3(W, T, D), body, true);
            Part("Plinth", new Vector3(0f, 0.03f, 0f), new Vector3(W, 0.06f, D), dark, true);
            Part("Inside_Back", new Vector3(0f, H * 0.5f, -D * 0.5f + T + 0.002f), new Vector3(W - 2f * T, H - 0.1f, 0.004f), dark, false);
            Part("Inside_Shelf", new Vector3(0f, 1.88f, -0.05f), new Vector3(W - 2f * T, 0.02f, 0.55f), body, false);
            Part("Inside_Hook", new Vector3(0f, 1.7f, -D * 0.5f + 0.06f), new Vector3(0.02f, 0.02f, 0.06f), dark, false);

            // Door: hinge on the +X front corner, opening outward (100°).
            Vector3 hingePos = root.TransformPoint(new Vector3(W * 0.5f - T, 0f, D * 0.5f - 0.02f));
            float leafW = W - 2f * T - 0.01f, leafH = H - 0.06f - T - 0.02f, gap = 0.06f;
            var door = Door(root, "LockerDoor_Hinge", hingePos, yaw + 90f, false, null, leafMat: body,
                leafSize: new Vector3(leafW, leafH, gap), openDelta: 100f, metalSound: true);
            if (door != null)
            {
                var hinge = door.transform;
                foreach (Transform c in hinge)
                    if (c.name.EndsWith("_Leaf"))
                        c.GetComponent<Renderer>().enabled = false; // the panels below draw the leaf (with its slit)
                // Hinge-local: the leaf spans z 0..-leafW, y gap..gap+leafH, x ±0.02; -X is the outer face.
                float zc = -leafW * 0.5f, slitLo = Eye - 0.05f, slitHi = Eye + 0.05f, slitW = 0.4f;
                void Panel(string n, float y0, float y1, float z0, float z1, Material m, float x = 0f, float thick = 0.04f)
                {
                    var go = Prim(hinge, PrimitiveType.Cube, n, new Vector3(x, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                        new Vector3(thick, y1 - y0, Mathf.Abs(z1 - z0)), m, false);
                    go.transform.localRotation = Quaternion.identity;
                }
                Panel("Panel_Lower", gap, slitLo, -leafW, 0f, body);
                Panel("Panel_Upper", slitHi, gap + leafH, -leafW, 0f, body);
                Panel("Panel_SlitL", slitLo, slitHi, -leafW, zc - slitW * 0.5f, body);
                Panel("Panel_SlitR", slitLo, slitHi, zc + slitW * 0.5f, 0f, body);
                Panel("Slit_LipTop", slitHi, slitHi + 0.012f, zc - slitW * 0.5f, zc + slitW * 0.5f, dark, 0f, 0.05f);
                Panel("Slit_LipBottom", slitLo - 0.012f, slitLo, zc - slitW * 0.5f, zc + slitW * 0.5f, dark, 0f, 0.05f);
                for (int i = 0; i < 3; i++) // decorative vent lines up top
                    Panel("VentLine", gap + leafH - 0.25f + i * 0.05f, gap + leafH - 0.235f + i * 0.05f, zc - 0.18f, zc + 0.18f, dark, -0.022f, 0.004f);
                Panel("Handle", 1.0f, 1.12f, -leafW + 0.05f, -leafW + 0.08f, dark, -0.035f, 0.03f);
                Panel("NameCard", 1.3f, 1.38f, zc - 0.07f, zc + 0.07f, Existing("MAT_Cafe_CupPaper"), -0.022f, 0.004f);
            }

            var cam = Child(root, "CameraPoint");
            cam.localPosition = new Vector3(0f, Eye, D * 0.5f - 0.27f);
            var exit = Child(root, "ExitPoint");
            exit.localPosition = new Vector3(-0.3f, 0f, D * 0.5f + 0.85f);
            var hide = root.gameObject.AddComponent<HideableLocker>();
            Set(hide, "door", door);
            Set(hide, "cameraPoint", cam);
            Set(hide, "exitPoint", exit);
            Set(hide, "player", Object.FindFirstObjectByType<PlayerInteractor>());
        }
    }
}
