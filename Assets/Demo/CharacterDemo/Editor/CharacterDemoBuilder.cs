using System.IO;
using AI;
using AI.Tools;
using UI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

namespace Demo.CharacterDemo.Editor
{
    /// <summary>
    /// Tools → AI → Build Character Demo: creates Assets/Demo/CharacterDemo/CharacterDemo.unity
    /// (four rooms, a character the agent controls, the chat) through Unity's API.
    /// Re-run it to rebuild the scene from scratch; generated assets go in Generated/.
    /// </summary>
    public static class CharacterDemoBuilder
    {
        private const string Root = "Assets/Demo/CharacterDemo";
        private const string Generated = Root + "/Generated";
        private const string ScenePath = Root + "/CharacterDemo.unity";
        private const string ChatUxmlPath = "Assets/UI Toolkit/ChatboxVisualTree.uxml";
        private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";

        private const float WallHeight = 1.5f;
        private const float WallThickness = 0.3f;

        [MenuItem("Tools/AI/Build Character Demo")]
        private static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[CharacterDemo] Exit Play mode first.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Rebuild Character Demo",
                    $"{ScenePath} already exists. Rebuild it from scratch?", "Rebuild", "Cancel"))
                return;

            var chatUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ChatUxmlPath);
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (chatUxml == null || panelSettings == null)
            {
                Debug.LogError($"[CharacterDemo] Missing {ChatUxmlPath} or {PanelSettingsPath}.");
                return;
            }

            EnsureFolder(Generated);
            EnsureFolder(Generated + "/Materials");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Camera.main;
            if (camera != null)
                camera.transform.SetPositionAndRotation(new Vector3(0f, 17f, -12f), Quaternion.Euler(55f, 0f, 0f));

            var level = BuildLevel();
            BuildPlaces();
            var toolSet = LoadOrCreate<ToolSet>(Generated + "/CharacterToolSet.asset");
            var character = BuildCharacter(toolSet);
            BakeNavMesh(level);
            BuildUi(panelSettings, chatUxml);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = character;
            Debug.Log($"[CharacterDemo] Built {ScenePath}. Start the agent server, press Play and chat.");
        }

        // ---- Level: 22 x 14 floor, four rooms joined by doorways ----
        //   storage (top middle) | kitchen (left) | hall (middle) | garden (right)
        private static GameObject BuildLevel()
        {
            var level = new GameObject("Level");
            var floorMat = Mat("Floor", new Color(0.55f, 0.55f, 0.58f));
            var wallMat = Mat("Wall", new Color(0.25f, 0.27f, 0.32f));

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(level.transform);
            floor.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            floor.transform.localScale = new Vector3(22f, 0.2f, 14f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            var walls = new GameObject("Walls").transform;
            walls.SetParent(level.transform);

            // Outer walls.
            Wall(walls, wallMat, new Vector2(-11, -7), new Vector2(11, -7));
            Wall(walls, wallMat, new Vector2(-11, 7), new Vector2(11, 7));
            Wall(walls, wallMat, new Vector2(-11, -7), new Vector2(-11, 7));
            Wall(walls, wallMat, new Vector2(11, -7), new Vector2(11, 7));

            // Kitchen | hall, doorway at z -2..0.
            Wall(walls, wallMat, new Vector2(-4, -7), new Vector2(-4, -2));
            Wall(walls, wallMat, new Vector2(-4, 0), new Vector2(-4, 7));

            // Hall | garden, doorway at z -2..0.
            Wall(walls, wallMat, new Vector2(4, -7), new Vector2(4, -2));
            Wall(walls, wallMat, new Vector2(4, 0), new Vector2(4, 7));

            // Hall | storage, doorway at x -1..1.
            Wall(walls, wallMat, new Vector2(-4, 3), new Vector2(-1, 3));
            Wall(walls, wallMat, new Vector2(1, 3), new Vector2(4, 3));

            return level;
        }

        private static void Wall(Transform parent, Material material, Vector2 from, Vector2 to)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.SetParent(parent);

            var mid = (from + to) / 2f;
            var length = Vector2.Distance(from, to) + WallThickness;
            var alongX = Mathf.Abs(to.x - from.x) > Mathf.Abs(to.y - from.y);
            wall.transform.localPosition = new Vector3(mid.x, WallHeight / 2f, mid.y);
            wall.transform.localScale = alongX
                ? new Vector3(length, WallHeight, WallThickness)
                : new Vector3(WallThickness, WallHeight, length);
            wall.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void BuildPlaces()
        {
            var places = new GameObject("Places").transform;
            Place(places, "kitchen", "A small kitchen with a stove and a table.",
                new Vector3(-7.5f, 0f, 0f), 3f, new Color(0.85f, 0.55f, 0.3f));
            Place(places, "hall", "The central hall. Doorways lead west, east and north.",
                new Vector3(0f, 0f, -2f), 3f, new Color(0.4f, 0.55f, 0.85f));
            Place(places, "storage", "A cramped storage room full of boxes.",
                new Vector3(0f, 0f, 5f), 1.8f, new Color(0.6f, 0.45f, 0.75f));
            Place(places, "garden", "A quiet garden with grass and a bench.",
                new Vector3(7.5f, 0f, 0f), 3f, new Color(0.4f, 0.75f, 0.4f));
        }

        private static void Place(Transform parent, string placeName, string description,
            Vector3 position, float radius, Color color)
        {
            // Flat disc marking the place. Not under Level, so it isn't baked into the NavMesh.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = $"Place: {placeName}";
            go.transform.SetParent(parent);
            go.transform.localPosition = position + Vector3.up * 0.01f;
            go.transform.localScale = new Vector3(radius * 2f, 0.005f, radius * 2f);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = Mat($"Place_{placeName}", color);

            var landmark = go.AddComponent<Landmark>();
            Set(landmark, "placeName", p => p.stringValue = placeName);
            Set(landmark, "description", p => p.stringValue = description);
            Set(landmark, "radius", p => p.floatValue = radius);
        }

        private static GameObject BuildCharacter(ToolSet toolSet)
        {
            var character = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            character.name = "Character";
            character.transform.position = new Vector3(0f, 1f, -3f);
            character.GetComponent<Renderer>().sharedMaterial = Mat("Character", new Color(0.95f, 0.8f, 0.25f));

            // A "nose" so you can see which way it faces.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            nose.transform.SetParent(character.transform);
            nose.transform.localPosition = new Vector3(0f, 0.5f, 0.45f);
            nose.transform.localScale = new Vector3(0.25f, 0.15f, 0.3f);
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.GetComponent<Renderer>().sharedMaterial = Mat("Nose", new Color(0.2f, 0.2f, 0.25f));

            character.AddComponent<DemoCharacter>(); // adds the NavMeshAgent too
            var nav = character.GetComponent<NavMeshAgent>();
            nav.baseOffset = 1f; // capsule pivot is at its centre
            nav.radius = 0.4f;
            nav.height = 2f;
            nav.speed = 3.5f;
            nav.angularSpeed = 720f;
            nav.acceleration = 20f;
            nav.stoppingDistance = 0.3f;

            var tools = character.AddComponent<CharacterTools>();
            Set(tools, "toolSet", p => p.objectReferenceValue = toolSet);

            var agent = character.AddComponent<Agent>();
            Set(agent, "toolSet", p => p.objectReferenceValue = toolSet);
            Set(agent, "maxSteps", p => p.intValue = 15);

            return character;
        }

        private static void BakeNavMesh(GameObject level)
        {
            var surface = level.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children; // floor + walls only
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.BuildNavMesh();

            var path = Generated + "/CharacterDemoNavMesh.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
        }

        private static void BuildUi(PanelSettings panelSettings, VisualTreeAsset chatUxml)
        {
            var overlay = new GameObject("World Overlay");
            var overlayDoc = overlay.AddComponent<UIDocument>();
            overlayDoc.panelSettings = panelSettings;
            overlayDoc.sortingOrder = -10; // under the chat
            overlay.AddComponent<WorldOverlay>();

            var chat = new GameObject("Chat");
            var chatDoc = chat.AddComponent<UIDocument>();
            chatDoc.panelSettings = panelSettings;
            chatDoc.visualTreeAsset = chatUxml;
            chatDoc.sortingOrder = 10;
            chat.AddComponent<ChatBox>();
            chat.AddComponent<AgentChatBridge>(); // finds the ChatBox and the Agent by itself
        }

        // ---- Helpers ----

        private static Material Mat(string name, Color color)
        {
            var path = $"{Generated}/Materials/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void Set(Object target, string property, System.Action<SerializedProperty> assign)
        {
            var serialized = new SerializedObject(target);
            var p = serialized.FindProperty(property);
            if (p == null)
            {
                Debug.LogError($"[CharacterDemo] {target.GetType().Name} has no serialized field '{property}'.");
                return;
            }

            assign(p);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
