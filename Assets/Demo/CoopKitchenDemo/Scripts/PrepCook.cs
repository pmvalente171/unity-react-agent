using System.ComponentModel;
using System.Threading;
using AI.Tools;
using Demo.KitchenDemo;
using UnityEngine;

namespace Demo.CoopKitchenDemo
{
    /// <summary>Prep cook: everything a <see cref="CoopCook"/> does, plus chopping.</summary>
    public class PrepCook : CoopCook
    {
        [AgentTool, Description("Walk to the chopping board and chop the raw ingredient you hold (not bread). Takes a few seconds.")]
        private async Awaitable<ToolResult> Chop(CancellationToken ct)
        {
            if (Hands.Held is not Food { CanChop: true } food)
                return ToolResult.Fail($"You need to hold a raw ingredient that isn't bread (you hold {Hands.Describe()}).");

            var board = Find<ChoppingBoard>();
            if (await WalkTo(board, ct) is { } error) return ToolResult.Fail(error);

            await board.Chop(food, ct);
            Hands.Refresh();
            return ToolResult.Ok($"You now hold {food.Name}.");
        }
    }
}
