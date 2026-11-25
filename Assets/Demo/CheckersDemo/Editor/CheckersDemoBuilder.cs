using AI;
using AI.Tools;
using Demo.Shared.Editor;
using UnityEditor;
using UnityEngine;

namespace Demo.CheckersDemo.Editor
{
    /// <summary>
    /// Tools → AI → Build Checkers Demo: creates Assets/Demo/CheckersDemo/CheckersDemo.unity. The player
    /// (Black) plays against an AI agent (White) that can consult a planner sub-agent; both agents show
    /// in the chat. Re-run it to rebuild the scene from scratch; generated assets go in Generated/.
    /// </summary>
    public static class CheckersDemoBuilder
    {
        private const string Root = "Assets/Demo/CheckersDemo";
        private const string Generated = Root + "/Generated";
        private const string ScenePath = Root + "/CheckersDemo.unity";

        [MenuItem("Tools/AI/Build Checkers Demo")]
        private static void Build()
        {
            if (!DemoBuilder.BeginScene(ScenePath, Generated, out var scene, out var chatUxml, out var panelSettings))
                return;

            // Shifted right so the board sits left of the chat.
            DemoBuilder.PlaceCamera(new Vector3(2f, 10.5f, -7.5f), 55f);
            BuildBoard();

            // The planner: an agent with no body, only a simulate tool on its own ToolSet.
            var plannerToolSet = DemoBuilder.LoadOrCreate<ToolSet>(Generated + "/PlannerToolSet.asset");
            var plannerObject = new GameObject("Planner (sub-agent)");
            var plannerTools = plannerObject.AddComponent<CheckersPlannerTools>();
            DemoBuilder.Set(plannerTools, "toolSet", p => p.objectReferenceValue = plannerToolSet);
            var planner = DemoBuilder.AddAgent(plannerObject, plannerToolSet, maxSteps: 12);

            // The AI player: reads the board, moves, and consults the planner through a SubAgentTool.
            var playerToolSet = DemoBuilder.LoadOrCreate<ToolSet>(Generated + "/WhiteToolSet.asset");
            var playerObject = new GameObject("White (AI player)");
            var playerTools = playerObject.AddComponent<CheckersPlayerTools>();
            DemoBuilder.Set(playerTools, "toolSet", p => p.objectReferenceValue = playerToolSet);
            DemoBuilder.Set(playerTools, "planner", p => p.objectReferenceValue = planner);
            var ai = DemoBuilder.AddAgent(playerObject, playerToolSet, maxSteps: 8);

            var game = new GameObject("Checkers Game").AddComponent<CheckersGame>();
            DemoBuilder.Set(game, "ai", p => p.objectReferenceValue = ai);
            DemoBuilder.Set(game, "blackMaterial", p => p.objectReferenceValue = DemoBuilder.Mat(Generated, "BlackPiece", new Color(0.15f, 0.15f, 0.17f)));
            DemoBuilder.Set(game, "whiteMaterial", p => p.objectReferenceValue = DemoBuilder.Mat(Generated, "WhitePiece", new Color(0.93f, 0.9f, 0.82f)));
            DemoBuilder.Set(game, "highlightMaterial", p => p.objectReferenceValue = DemoBuilder.Mat(Generated, "Highlight", new Color(0.95f, 0.8f, 0.2f)));

            // One chat, two agents. Only White listens to the player; the planner takes tasks from White.
            var chat = DemoBuilder.BuildUi(panelSettings, chatUxml);
            WireBridge(chat.GetComponent<AgentChatBridge>(), ai, "White", listenToPlayer: true);
            WireBridge(chat.AddComponent<AgentChatBridge>(), planner, "Planner", listenToPlayer: false);

            DemoBuilder.EndScene(scene, ScenePath, game.gameObject, "Checkers Demo");
        }

        // Square (f, r) is centred at (f - 3.5, 0, r - 3.5); a1 (dark) is bottom-left, nearest the camera.
        private static void BuildBoard()
        {
            var board = new GameObject("Board").transform;
            DemoBuilder.Box(board, "Base", new Vector3(0f, -0.12f, 0f), new Vector3(8.6f, 0.2f, 8.6f),
                DemoBuilder.Mat(Generated, "BoardBase", new Color(0.35f, 0.22f, 0.12f))); // its collider takes the clicks

            var dark = DemoBuilder.Mat(Generated, "DarkSquare", new Color(0.3f, 0.45f, 0.3f));
            var light = DemoBuilder.Mat(Generated, "LightSquare", new Color(0.92f, 0.88f, 0.76f));
            for (var rank = 0; rank < 8; rank++)
            for (var file = 0; file < 8; file++)
            {
                var name = $"{(char)('a' + file)}{rank + 1}";
                DemoBuilder.Box(board, name, new Vector3(file - 3.5f, -0.01f, rank - 3.5f), new Vector3(1f, 0.02f, 1f),
                    Demo.CheckersDemo.Board.IsDark(file, rank) ? dark : light, keepCollider: false);
            }
        }

        private static void WireBridge(AgentChatBridge bridge, Agent agent, string label, bool listenToPlayer)
        {
            DemoBuilder.Set(bridge, "agent", p => p.objectReferenceValue = agent);
            DemoBuilder.Set(bridge, "label", p => p.stringValue = label);
            DemoBuilder.Set(bridge, "listenToPlayer", p => p.boolValue = listenToPlayer);
        }
    }
}
