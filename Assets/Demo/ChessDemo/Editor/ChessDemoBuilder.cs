using AI;
using AI.Tools;
using Demo.Shared.Editor;
using UnityEditor;
using UnityEngine;

namespace Demo.ChessDemo.Editor
{
    /// <summary>
    /// Tools → AI → Build Chess Demo: creates Assets/Demo/ChessDemo/ChessDemo.unity. The player (White) plays
    /// against an AI agent (Black) that can consult a planner sub-agent; both agents show in the chat.
    /// Pieces use the meshes in Meshes/chesssetcycles.blend. Re-run it to rebuild the scene from scratch.
    /// </summary>
    public static class ChessDemoBuilder
    {
        private const string Root = "Assets/Demo/ChessDemo";
        private const string Generated = Root + "/Generated";
        private const string ScenePath = Root + "/ChessDemo.unity";
        private const string PiecesModelPath = Root + "/Meshes/chesssetcycles.blend";

        [MenuItem("Tools/AI/Build Chess Demo")]
        private static void Build()
        {
            var piecesModel = AssetDatabase.LoadAssetAtPath<GameObject>(PiecesModelPath);
            if (piecesModel == null)
            {
                Debug.LogError($"[ChessDemoBuilder] Missing {PiecesModelPath} (importing .blend files needs Blender installed).");
                return;
            }

            if (!DemoBuilder.BeginScene(ScenePath, Generated, out var scene, out var chatUxml, out var panelSettings))
                return;

            // Shifted right so the board sits left of the chat.
            DemoBuilder.PlaceCamera(new Vector3(2f, 10.5f, -7.5f), 55f);
            BuildBoard();

            // The planner: an agent with no body, only a simulate tool on its own ToolSet.
            var plannerToolSet = DemoBuilder.LoadOrCreate<ToolSet>(Generated + "/PlannerToolSet.asset");
            var plannerObject = new GameObject("Planner (sub-agent)");
            var plannerTools = plannerObject.AddComponent<ChessPlannerTools>();
            DemoBuilder.Set(plannerTools, "toolSet", p => p.objectReferenceValue = plannerToolSet);
            var planner = DemoBuilder.AddAgent(plannerObject, plannerToolSet, maxSteps: 12);

            // The AI player: reads the board, moves, and consults the planner through a SubAgentTool.
            var playerToolSet = DemoBuilder.LoadOrCreate<ToolSet>(Generated + "/BlackToolSet.asset");
            var playerObject = new GameObject("Black (AI player)");
            var playerTools = playerObject.AddComponent<ChessPlayerTools>();
            DemoBuilder.Set(playerTools, "toolSet", p => p.objectReferenceValue = playerToolSet);
            DemoBuilder.Set(playerTools, "planner", p => p.objectReferenceValue = planner);
            var ai = DemoBuilder.AddAgent(playerObject, playerToolSet, maxSteps: 8);

            var game = new GameObject("Chess Game").AddComponent<ChessGame>();
            DemoBuilder.Set(game, "ai", p => p.objectReferenceValue = ai);
            DemoBuilder.Set(game, "piecesModel", p => p.objectReferenceValue = piecesModel);
            DemoBuilder.Set(game, "whiteMaterial", p => p.objectReferenceValue = DemoBuilder.Mat(Generated, "WhitePiece", new Color(0.93f, 0.9f, 0.82f)));
            DemoBuilder.Set(game, "blackMaterial", p => p.objectReferenceValue = DemoBuilder.Mat(Generated, "BlackPiece", new Color(0.15f, 0.15f, 0.17f)));
            DemoBuilder.Set(game, "highlightMaterial", p => p.objectReferenceValue = DemoBuilder.Mat(Generated, "Highlight", new Color(0.95f, 0.8f, 0.2f)));

            // One chat, two agents. Only Black listens to the player; the planner takes tasks from Black.
            var chat = DemoBuilder.BuildUi(panelSettings, chatUxml);
            WireBridge(chat.GetComponent<AgentChatBridge>(), ai, "Black", listenToPlayer: true);
            WireBridge(chat.AddComponent<AgentChatBridge>(), planner, "Planner", listenToPlayer: false);

            DemoBuilder.EndScene(scene, ScenePath, game.gameObject, "Chess Demo");
        }

        // Square (f, r) is centred at (f - 3.5, 0, r - 3.5); a1 (dark) is bottom-left, nearest the camera.
        private static void BuildBoard()
        {
            var board = new GameObject("Board").transform;
            DemoBuilder.Box(board, "Base", new Vector3(0f, -0.12f, 0f), new Vector3(8.6f, 0.2f, 8.6f),
                DemoBuilder.Mat(Generated, "BoardBase", new Color(0.35f, 0.22f, 0.12f))); // its collider takes the clicks

            var dark = DemoBuilder.Mat(Generated, "DarkSquare", new Color(0.45f, 0.32f, 0.22f));
            var light = DemoBuilder.Mat(Generated, "LightSquare", new Color(0.92f, 0.85f, 0.7f));
            for (var r = 0; r < 8; r++)
            for (var f = 0; f < 8; f++)
                DemoBuilder.Box(board, $"{(char)('a' + f)}{r + 1}", new Vector3(f - 3.5f, -0.01f, r - 3.5f), new Vector3(1f, 0.02f, 1f),
                    (f + r) % 2 == 0 ? dark : light, keepCollider: false);
        }

        private static void WireBridge(AgentChatBridge bridge, Agent agent, string label, bool listenToPlayer)
        {
            DemoBuilder.Set(bridge, "agent", p => p.objectReferenceValue = agent);
            DemoBuilder.Set(bridge, "label", p => p.stringValue = label);
            DemoBuilder.Set(bridge, "listenToPlayer", p => p.boolValue = listenToPlayer);
        }
    }
}
