using AI;
using AI.Tools;
using Demo.KitchenDemo;
using Demo.Shared.Editor;
using UnityEditor;
using UnityEngine;

namespace Demo.CoopKitchenDemo.Editor
{
    /// <summary>
    /// Tools → AI → Build Coop Kitchen Demo: creates Assets/Demo/CoopKitchenDemo/CoopKitchenDemo.unity.
    /// Two agents share one kitchen: a prep zone (left), a line zone (right) and a middle zone with the
    /// pass counter and the plan board. Reuses the Kitchen Demo's food and stations without changing them.
    /// Re-run it to rebuild the scene from scratch; generated assets go in Generated/.
    /// </summary>
    public static class CoopKitchenDemoBuilder
    {
        private const string Root = "Assets/Demo/CoopKitchenDemo";
        private const string Generated = Root + "/Generated";
        private const string ScenePath = Root + "/CoopKitchenDemo.unity";

        // Room: 20 x 10, walls at x = ±10 and z = ±5. Zones split at x = ±3.5.
        private static readonly Vector3 StationSize = new(1.2f, 1f, 1.2f);
        private static readonly Vector3 FaceSouth = new(0f, 0f, -1.3f);
        private static readonly Vector3 FaceNorth = new(0f, 0f, 1.3f);
        private static readonly Vector3 FaceEast = new(1.3f, 0f, 0f);
        private static readonly Vector3 FaceWest = new(-1.3f, 0f, 0f);

        [MenuItem("Tools/AI/Build Coop Kitchen Demo")]
        private static void Build()
        {
            if (!DemoBuilder.BeginScene(ScenePath, Generated, out var scene, out var chatUxml, out var panelSettings))
                return;

            DemoBuilder.PlaceCamera(new Vector3(0f, 16f, -11f), 58f);

            var level = new GameObject("Level");
            var prep = Zone(level.transform, "Prep Zone", -6.75f, 6.5f, new Color(0.62f, 0.7f, 0.62f));
            var middle = Zone(level.transform, "Middle Zone", 0f, 7f, new Color(0.72f, 0.7f, 0.64f));
            var line = Zone(level.transform, "Line Zone", 6.75f, 6.5f, new Color(0.7f, 0.64f, 0.6f));
            BuildWalls(level.transform);

            AddCrate(prep, Ingredient.Tomato, -9f, new Color(0.9f, 0.2f, 0.15f));
            AddCrate(prep, Ingredient.Lettuce, -7.5f, new Color(0.35f, 0.75f, 0.3f));
            AddCrate(prep, Ingredient.Meat, -6f, new Color(0.8f, 0.4f, 0.45f));
            AddStation<ChoppingBoard>(prep, "chopping board", new Vector3(-9.2f, 0f, -0.5f), FaceEast, new Color(0.6f, 0.45f, 0.3f));
            AddStation<Trash>(prep, "prep trash", new Vector3(-9.2f, 0f, -3.5f), FaceEast, new Color(0.35f, 0.4f, 0.35f), Vector3.one * 0.9f);

            AddCrate(line, Ingredient.Bread, 8.5f, new Color(0.85f, 0.7f, 0.4f));
            AddStation<Stove>(line, "stove", new Vector3(9.2f, 0f, 1.5f), FaceWest, new Color(0.2f, 0.2f, 0.22f));
            AddStation<Trash>(line, "line trash", new Vector3(9.2f, 0f, -1.5f), FaceWest, new Color(0.35f, 0.4f, 0.35f), Vector3.one * 0.9f);
            AddStation<PlateCounter>(line, "plate counter", new Vector3(5.5f, 0f, -4.2f), FaceNorth, new Color(0.85f, 0.85f, 0.88f));
            AddStation<ServingHatch>(line, "serving hatch", new Vector3(8f, 0f, -4.2f), FaceNorth, new Color(0.95f, 0.75f, 0.2f));

            // Middle: each cook stands on its own side of these (CoopCook.sharedStandOffset).
            AddStation<PassCounter>(middle, "pass counter", new Vector3(0f, 0f, -1f), Vector3.zero, new Color(0.55f, 0.75f, 0.9f), new Vector3(1.2f, 1f, 1.6f));
            AddStation<PlanBoard>(middle, "plan board", new Vector3(0f, 0f, 2.5f), Vector3.zero, new Color(0.95f, 0.95f, 0.85f), new Vector3(0.4f, 1.6f, 1.4f));

            var prepCook = Cook<PrepCook>("Prep Cook", "prep cook", new Vector3(-6.5f, 0f, 0f), new Color(0.4f, 0.65f, 0.95f), prep, middle, -1.2f,
                "You work the prep zone (left): the tomato, lettuce and meat crates and the chopping board. " +
                "You take and chop ingredients and pass them to your partner.", out var prepAgent);
            Cook<LineCook>("Line Cook", "line cook", new Vector3(6.5f, 0f, 0f), new Color(0.95f, 0.55f, 0.35f), line, middle, 1.2f,
                "You work the line zone (right): the bread crate, the stove, the plate counter and the serving hatch. " +
                "You cook, plate and serve.", out var lineAgent);

            new GameObject("Coop Director").AddComponent<CoopDirector>(); // finds the cooks by itself

            DemoBuilder.BakeNavMesh(level, Generated + "/CoopKitchenDemoNavMesh.asset");

            // One chat, one bridge per agent; the label tells their messages apart.
            var chat = DemoBuilder.BuildUi(panelSettings, chatUxml);
            WireBridge(chat.GetComponent<AgentChatBridge>(), prepAgent, "Prep");
            WireBridge(chat.AddComponent<AgentChatBridge>(), lineAgent, "Line");

            DemoBuilder.EndScene(scene, ScenePath, prepCook, "Coop Kitchen Demo");
        }

        private static Transform Zone(Transform level, string name, float centerX, float width, Color color)
        {
            var zone = new GameObject(name).transform;
            zone.SetParent(level);
            DemoBuilder.Box(zone, "Floor", new Vector3(centerX, -0.1f, 0f), new Vector3(width, 0.2f, 10f),
                DemoBuilder.Mat(Generated, name.Replace(" ", ""), color));
            return zone;
        }

        private static void BuildWalls(Transform level)
        {
            var walls = new GameObject("Walls").transform;
            walls.SetParent(level);
            var mat = DemoBuilder.Mat(Generated, "Wall", new Color(0.3f, 0.32f, 0.38f));
            DemoBuilder.Wall(walls, mat, new Vector2(-10, -5), new Vector2(10, -5));
            DemoBuilder.Wall(walls, mat, new Vector2(-10, 5), new Vector2(10, 5));
            DemoBuilder.Wall(walls, mat, new Vector2(-10, -5), new Vector2(-10, 5));
            DemoBuilder.Wall(walls, mat, new Vector2(10, -5), new Vector2(10, 5));

            // Short dividers mark the zones; the gap between them leads to the middle.
            foreach (var x in new[] { -3.5f, 3.5f })
            {
                DemoBuilder.Wall(walls, mat, new Vector2(x, -5), new Vector2(x, -2.8f));
                DemoBuilder.Wall(walls, mat, new Vector2(x, 3.8f), new Vector2(x, 5));
            }
        }

        private static GameObject Cook<T>(string objectName, string cookName, Vector3 position, Color color,
            Transform zone, Transform middle, float sideX, string role, out Agent agent) where T : CoopCook
        {
            var id = objectName.Replace(" ", "");
            var toolSet = DemoBuilder.LoadOrCreate<ToolSet>($"{Generated}/{id}ToolSet.asset"); // one ToolSet per agent

            var go = DemoBuilder.Character(Generated, position, color);
            go.name = objectName;
            go.GetComponent<Renderer>().sharedMaterial = DemoBuilder.Mat(Generated, id, color);
            go.AddComponent<Hands>();

            var cook = go.AddComponent<T>();
            DemoBuilder.Set(cook, "toolSet", p => p.objectReferenceValue = toolSet);
            DemoBuilder.Set(cook, "cookName", p => p.stringValue = cookName);
            DemoBuilder.Set(cook, "role", p => p.stringValue = role);
            DemoBuilder.Set(cook, "zone", p => p.objectReferenceValue = zone);
            DemoBuilder.Set(cook, "middle", p => p.objectReferenceValue = middle);
            DemoBuilder.Set(cook, "sharedStandOffset", p => p.vector3Value = new Vector3(sideX, 0f, 0f));

            agent = DemoBuilder.AddAgent(go, toolSet, maxSteps: 20);
            return go;
        }

        private static void WireBridge(AgentChatBridge bridge, Agent agent, string label)
        {
            DemoBuilder.Set(bridge, "agent", p => p.objectReferenceValue = agent);
            DemoBuilder.Set(bridge, "label", p => p.stringValue = label);
        }

        private static void AddCrate(Transform zone, Ingredient ingredient, float x, Color color)
        {
            var crate = AddStation<Crate>(zone, $"{ingredient.ToString().ToLowerInvariant()} crate",
                new Vector3(x, 0f, 4.2f), FaceSouth, color);
            DemoBuilder.Set(crate, "ingredient", p => p.enumValueIndex = (int)ingredient);
        }

        private static T AddStation<T>(Transform zone, string stationName, Vector3 floorPosition,
            Vector3 standOffset, Color color, Vector3? size = null) where T : Station
        {
            var s = size ?? StationSize;
            var box = DemoBuilder.Box(zone, stationName, floorPosition + Vector3.up * (s.y / 2f), s,
                DemoBuilder.Mat(Generated, "Station_" + stationName.Replace(' ', '_'), color));

            var station = box.AddComponent<T>();
            DemoBuilder.Set(station, "stationName", p => p.stringValue = stationName);
            DemoBuilder.Set(station, "standOffset", p => p.vector3Value = standOffset);
            return station;
        }
    }
}
