using AI.Tools;
using Demo.Shared.Editor;
using UnityEditor;
using UnityEngine;

namespace Demo.KitchenDemo.Editor
{
    /// <summary>
    /// Tools → AI → Build Kitchen Demo: creates Assets/Demo/KitchenDemo/KitchenDemo.unity
    /// (a one-room kitchen with stations, a cook the agent controls, the chat) through Unity's API.
    /// Re-run it to rebuild the scene from scratch; generated assets go in Generated/.
    /// </summary>
    public static class KitchenDemoBuilder
    {
        private const string Root = "Assets/Demo/KitchenDemo";
        private const string Generated = Root + "/Generated";
        private const string ScenePath = Root + "/KitchenDemo.unity";

        // Room: 14 x 10, walls at x = ±7 and z = ±5. Stations stand against the walls.
        private static readonly Vector3 StationSize = new(1.2f, 1f, 1.2f);
        private static readonly Vector3 FaceSouth = new(0f, 0f, -1.3f); // stand south of it (north wall)
        private static readonly Vector3 FaceNorth = new(0f, 0f, 1.3f);
        private static readonly Vector3 FaceEast = new(1.3f, 0f, 0f);
        private static readonly Vector3 FaceWest = new(-1.3f, 0f, 0f);

        [MenuItem("Tools/AI/Build Kitchen Demo")]
        private static void Build()
        {
            if (!DemoBuilder.BeginScene(ScenePath, Generated, out var scene, out var chatUxml, out var panelSettings))
                return;

            DemoBuilder.PlaceCamera(new Vector3(0f, 13f, -10f), 55f);

            var level = BuildRoom();
            BuildStations(level.transform);

            var toolSet = DemoBuilder.LoadOrCreate<ToolSet>(Generated + "/KitchenToolSet.asset");
            var cook = DemoBuilder.Character(Generated, Vector3.zero, new Color(0.95f, 0.95f, 0.95f));
            cook.name = "Cook";
            cook.AddComponent<Hands>();
            var tools = cook.AddComponent<KitchenTools>();
            DemoBuilder.Set(tools, "toolSet", p => p.objectReferenceValue = toolSet);
            DemoBuilder.AddAgent(cook, toolSet, maxSteps: 25);

            // Sends orders to the agent (finds it by itself) and shows the order board.
            new GameObject("Kitchen Director").AddComponent<KitchenDirector>();

            DemoBuilder.BakeNavMesh(level, Generated + "/KitchenDemoNavMesh.asset");
            DemoBuilder.BuildUi(panelSettings, chatUxml);
            DemoBuilder.EndScene(scene, ScenePath, cook, "Kitchen Demo");
        }

        private static GameObject BuildRoom()
        {
            var level = new GameObject("Level");
            DemoBuilder.Floor(level.transform, new Vector2(14f, 10f),
                DemoBuilder.Mat(Generated, "Floor", new Color(0.7f, 0.68f, 0.62f)));

            var walls = new GameObject("Walls").transform;
            walls.SetParent(level.transform);
            var wallMat = DemoBuilder.Mat(Generated, "Wall", new Color(0.3f, 0.32f, 0.38f));
            DemoBuilder.Wall(walls, wallMat, new Vector2(-7, -5), new Vector2(7, -5));
            DemoBuilder.Wall(walls, wallMat, new Vector2(-7, 5), new Vector2(7, 5));
            DemoBuilder.Wall(walls, wallMat, new Vector2(-7, -5), new Vector2(-7, 5));
            DemoBuilder.Wall(walls, wallMat, new Vector2(7, -5), new Vector2(7, 5));
            return level;
        }

        private static void BuildStations(Transform level)
        {
            // Under Level, so the NavMesh walks around them.
            var stations = new GameObject("Stations").transform;
            stations.SetParent(level);

            // North wall: ingredient crates.
            AddCrate(stations, Ingredient.Tomato, -4.5f, new Color(0.9f, 0.2f, 0.15f));
            AddCrate(stations, Ingredient.Lettuce, -2.5f, new Color(0.35f, 0.75f, 0.3f));
            AddCrate(stations, Ingredient.Bread, -0.5f, new Color(0.85f, 0.7f, 0.4f));
            AddCrate(stations, Ingredient.Meat, 1.5f, new Color(0.8f, 0.4f, 0.45f));

            // West wall: chopping board. East wall: stove and trash.
            AddStation<ChoppingBoard>(stations, "chopping board", new Vector3(-6.2f, 0f, 1f), FaceEast,
                new Color(0.6f, 0.45f, 0.3f));
            AddStation<Stove>(stations, "stove", new Vector3(6.2f, 0f, 1.5f), FaceWest,
                new Color(0.2f, 0.2f, 0.22f));
            AddStation<Trash>(stations, "trash", new Vector3(6.2f, 0f, -2f), FaceWest,
                new Color(0.35f, 0.4f, 0.35f), new Vector3(0.9f, 0.9f, 0.9f));

            // South wall: plate counter and serving hatch.
            AddStation<PlateCounter>(stations, "plate counter", new Vector3(-2.5f, 0f, -4.2f), FaceNorth,
                new Color(0.85f, 0.85f, 0.88f));
            AddStation<ServingHatch>(stations, "serving hatch", new Vector3(2.5f, 0f, -4.2f), FaceNorth,
                new Color(0.95f, 0.75f, 0.2f));
        }

        private static void AddCrate(Transform parent, Ingredient ingredient, float x, Color color)
        {
            var crate = AddStation<Crate>(parent, $"{ingredient.ToString().ToLowerInvariant()} crate",
                new Vector3(x, 0f, 4.2f), FaceSouth, color);
            DemoBuilder.Set(crate, "ingredient", p => p.enumValueIndex = (int)ingredient);
        }

        private static T AddStation<T>(Transform parent, string stationName, Vector3 floorPosition,
            Vector3 standOffset, Color color, Vector3? size = null) where T : Station
        {
            var s = size ?? StationSize;
            var box = DemoBuilder.Box(parent, stationName, floorPosition + Vector3.up * (s.y / 2f), s,
                DemoBuilder.Mat(Generated, "Station_" + stationName.Replace(' ', '_'), color));

            var station = box.AddComponent<T>();
            DemoBuilder.Set(station, "stationName", p => p.stringValue = stationName);
            DemoBuilder.Set(station, "standOffset", p => p.vector3Value = standOffset);
            return station;
        }
    }
}
