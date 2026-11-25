using System;
using System.IO;
using AI;
using UI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Demo.Shared.Editor
{
    /// <summary>
    /// Helpers shared by the demo scene builders (Tools → AI → Build ...). Everything goes through
    /// Unity's API, so scenes are never hand-edited. Generated assets go in each demo's Generated/.
    /// </summary>
    public static class DemoBuilder
    {
        public const string ChatUxmlPath = "Assets/UI Toolkit/ChatboxVisualTree.uxml";
        public const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";

        /// <summary>
        /// Checks Play mode, offers to save open scenes, confirms overwriting, loads the chat UI assets
        /// and opens a new scene with a camera and a light. False if anything stops the build.
        /// </summary>
        public static bool BeginScene(string scenePath, string generatedFolder, out Scene scene,
            out VisualTreeAsset chatUxml, out PanelSettings panelSettings)
        {
            scene = default;
            chatUxml = null;
            panelSettings = null;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[DemoBuilder] Exit Play mode first.");
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            if (File.Exists(scenePath) && !EditorUtility.DisplayDialog("Rebuild demo scene",
                    $"{scenePath} already exists. Rebuild it from scratch?", "Rebuild", "Cancel"))
                return false;

            chatUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ChatUxmlPath);
            panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (chatUxml == null || panelSettings == null)
            {
                Debug.LogError($"[DemoBuilder] Missing {ChatUxmlPath} or {PanelSettingsPath}.");
                return false;
            }

            EnsureFolder(generatedFolder);
            EnsureFolder(generatedFolder + "/Materials");
            scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            return true;
        }

        public static void PlaceCamera(Vector3 position, float pitch)
        {
            var camera = Camera.main;
            if (camera != null) camera.transform.SetPositionAndRotation(position, Quaternion.Euler(pitch, 0f, 0f));
        }

        /// <summary>Flat floor centred on the origin, top surface at y = 0.</summary>
        public static GameObject Floor(Transform parent, Vector2 size, Material material)
        {
            var floor = Box(parent, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(size.x, 0.2f, size.y), material);
            return floor;
        }

        /// <summary>Wall along X or Z between two floor points (x, z).</summary>
        public static void Wall(Transform parent, Material material, Vector2 from, Vector2 to,
            float height = 1.5f, float thickness = 0.3f)
        {
            var mid = (from + to) / 2f;
            var length = Vector2.Distance(from, to) + thickness;
            var alongX = Mathf.Abs(to.x - from.x) > Mathf.Abs(to.y - from.y);
            Box(parent, "Wall", new Vector3(mid.x, height / 2f, mid.y),
                alongX ? new Vector3(length, height, thickness) : new Vector3(thickness, height, length), material);
        }

        public static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale,
            Material material, bool keepCollider = true)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent);
            box.transform.localPosition = position;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider) Object.DestroyImmediate(box.GetComponent<Collider>());
            return box;
        }

        /// <summary>Capsule with a "nose", a configured NavMeshAgent and a <see cref="DemoCharacter"/>.</summary>
        public static GameObject Character(string generatedFolder, Vector3 floorPosition, Color color)
        {
            var character = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            character.name = "Character";
            character.transform.position = floorPosition + Vector3.up; // capsule pivot is at its centre
            character.GetComponent<Renderer>().sharedMaterial = Mat(generatedFolder, "Character", color);

            var nose = Box(character.transform, "Nose", new Vector3(0f, 0.5f, 0.45f), new Vector3(0.25f, 0.15f, 0.3f),
                Mat(generatedFolder, "Nose", new Color(0.2f, 0.2f, 0.25f)), keepCollider: false);
            nose.transform.localRotation = Quaternion.identity;

            character.AddComponent<DemoCharacter>(); // adds the NavMeshAgent too
            var nav = character.GetComponent<NavMeshAgent>();
            nav.baseOffset = 1f;
            nav.radius = 0.4f;
            nav.height = 2f;
            nav.speed = 3.5f;
            nav.angularSpeed = 720f;
            nav.acceleration = 20f;
            nav.stoppingDistance = 0.3f;
            return character;
        }

        /// <summary>Adds the agent, wired to <paramref name="toolSet"/>.</summary>
        public static Agent AddAgent(GameObject owner, Object toolSet, int maxSteps)
        {
            var agent = owner.AddComponent<Agent>();
            Set(agent, "toolSet", p => p.objectReferenceValue = toolSet);
            Set(agent, "maxSteps", p => p.intValue = maxSteps);
            return agent;
        }

        /// <summary>Bakes a NavMesh from <paramref name="level"/>'s children and saves it as an asset.</summary>
        public static void BakeNavMesh(GameObject level, string assetPath)
        {
            var surface = level.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.BuildNavMesh();

            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
        }

        /// <summary>World-space labels overlay plus the chat (which finds the Agent by itself).</summary>
        public static void BuildUi(PanelSettings panelSettings, VisualTreeAsset chatUxml)
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
            chat.AddComponent<AgentChatBridge>();
        }

        public static void EndScene(Scene scene, string scenePath, GameObject select, string demoName)
        {
            EditorSceneManager.SaveScene(scene, scenePath);
            Selection.activeGameObject = select;
            Debug.Log($"[DemoBuilder] Built {scenePath}. Start the agent server, press Play and chat. ({demoName})");
        }

        // ---- Assets and serialized fields ----

        public static Material Mat(string generatedFolder, string name, Color color)
        {
            var path = $"{generatedFolder}/Materials/{name}.mat";
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

        public static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>Sets a private [SerializeField] on a component or asset.</summary>
        public static void Set(Object target, string property, Action<SerializedProperty> assign)
        {
            var serialized = new SerializedObject(target);
            var p = serialized.FindProperty(property);
            if (p == null)
            {
                Debug.LogError($"[DemoBuilder] {target.GetType().Name} has no serialized field '{property}'.");
                return;
            }

            assign(p);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
