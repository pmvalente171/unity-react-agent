using AI.Tools;
using Demo.Shared.Editor;
using UnityEditor;
using UnityEngine;

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

        [MenuItem("Tools/AI/Build Character Demo")]
        private static void Build()
        {
            if (!DemoBuilder.BeginScene(ScenePath, Generated, out var scene, out var chatUxml, out var panelSettings))
                return;

            DemoBuilder.PlaceCamera(new Vector3(0f, 17f, -12f), 55f);

            var level = BuildLevel();
            BuildPlaces();

            var toolSet = DemoBuilder.LoadOrCreate<ToolSet>(Generated + "/CharacterToolSet.asset");
            var character = DemoBuilder.Character(Generated, new Vector3(0f, 0f, -3f), new Color(0.95f, 0.8f, 0.25f));
            var tools = character.AddComponent<CharacterTools>();
            DemoBuilder.Set(tools, "toolSet", p => p.objectReferenceValue = toolSet);
            DemoBuilder.AddAgent(character, toolSet, maxSteps: 15);

            DemoBuilder.BakeNavMesh(level, Generated + "/CharacterDemoNavMesh.asset");
            DemoBuilder.BuildUi(panelSettings, chatUxml);
            DemoBuilder.EndScene(scene, ScenePath, character, "Character Demo");
        }

        // 22 x 14 floor, four rooms joined by doorways:
        //   storage (top middle) | kitchen (left) | hall (middle) | garden (right)
        private static GameObject BuildLevel()
        {
            var level = new GameObject("Level");
            var wallMat = DemoBuilder.Mat(Generated, "Wall", new Color(0.25f, 0.27f, 0.32f));
            DemoBuilder.Floor(level.transform, new Vector2(22f, 14f),
                DemoBuilder.Mat(Generated, "Floor", new Color(0.55f, 0.55f, 0.58f)));

            var walls = new GameObject("Walls").transform;
            walls.SetParent(level.transform);

            // Outer walls.
            DemoBuilder.Wall(walls, wallMat, new Vector2(-11, -7), new Vector2(11, -7));
            DemoBuilder.Wall(walls, wallMat, new Vector2(-11, 7), new Vector2(11, 7));
            DemoBuilder.Wall(walls, wallMat, new Vector2(-11, -7), new Vector2(-11, 7));
            DemoBuilder.Wall(walls, wallMat, new Vector2(11, -7), new Vector2(11, 7));

            // Kitchen | hall, doorway at z -2..0.
            DemoBuilder.Wall(walls, wallMat, new Vector2(-4, -7), new Vector2(-4, -2));
            DemoBuilder.Wall(walls, wallMat, new Vector2(-4, 0), new Vector2(-4, 7));

            // Hall | garden, doorway at z -2..0.
            DemoBuilder.Wall(walls, wallMat, new Vector2(4, -7), new Vector2(4, -2));
            DemoBuilder.Wall(walls, wallMat, new Vector2(4, 0), new Vector2(4, 7));

            // Hall | storage, doorway at x -1..1.
            DemoBuilder.Wall(walls, wallMat, new Vector2(-4, 3), new Vector2(-1, 3));
            DemoBuilder.Wall(walls, wallMat, new Vector2(1, 3), new Vector2(4, 3));

            return level;
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
            go.GetComponent<Renderer>().sharedMaterial = DemoBuilder.Mat(Generated, $"Place_{placeName}", color);

            var landmark = go.AddComponent<Landmark>();
            DemoBuilder.Set(landmark, "placeName", p => p.stringValue = placeName);
            DemoBuilder.Set(landmark, "description", p => p.stringValue = description);
            DemoBuilder.Set(landmark, "radius", p => p.floatValue = radius);
        }
    }
}
