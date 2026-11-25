using System.ComponentModel;
using System.Threading;
using AI.Tools;
using Demo.KitchenDemo;
using UnityEngine;

namespace Demo.CoopKitchenDemo
{
    /// <summary>Line cook: everything a <see cref="CoopCook"/> does, plus cooking, plating and serving.</summary>
    public class LineCook : CoopCook
    {
        [AgentTool, Description("Walk to the stove, cook the chopped meat you hold, wait until it's done and take it back.")]
        private async Awaitable<ToolResult> Cook(CancellationToken ct)
        {
            if (Hands.Held is not Food { CanCook: true })
                return ToolResult.Fail($"Only chopped meat can be cooked (you hold {Hands.Describe()}).");

            var stove = Find<Stove>();
            if (!stove.IsEmpty) return ToolResult.Fail($"The stove is busy: {stove.Status}.");
            if (await WalkTo(stove, ct) is { } error) return ToolResult.Fail(error);

            stove.Put((Food)Hands.Release());
            try
            {
                while (stove.IsCooking) await Awaitable.NextFrameAsync(ct);
            }
            finally
            {
                Hands.Hold(stove.Take()); // also when stopped: never leave food on the stove to burn
            }

            return ToolResult.Ok($"You hold {Hands.Describe()}.");
        }

        [AgentTool, Description("Walk to the plate counter and put the food you hold on the plate.")]
        private async Awaitable<ToolResult> PutOnPlate(CancellationToken ct)
        {
            if (Hands.Held is not Food) return ToolResult.Fail($"You need to hold food (you hold {Hands.Describe()}).");

            var counter = Find<PlateCounter>();
            if (counter.Plate == null) return ToolResult.Fail("There is no plate on the counter.");
            if (await WalkTo(counter, ct) is { } error) return ToolResult.Fail(error);

            counter.Plate.Contents.Add((Food)Hands.Release());
            return ToolResult.Ok($"The counter now has a {counter.Plate.Name}.");
        }

        [AgentTool, Description("Take the plate from the plate counter and serve it at the hatch. Your hands must be empty " +
                                "and the plate must match an ordered recipe exactly.")]
        private async Awaitable<ToolResult> Serve(CancellationToken ct)
        {
            if (!Hands.IsEmpty) return HandsFull();

            var counter = Find<PlateCounter>();
            if (counter.Plate == null) return ToolResult.Fail("There is no plate on the counter.");
            if (await WalkTo(counter, ct) is { } error) return ToolResult.Fail(error);

            var plate = counter.TakePlate();
            Hands.Hold(plate);
            var hatch = Find<ServingHatch>();
            if (await WalkTo(hatch, ct) is { } walkError) return ToolResult.Fail($"{walkError} You hold the {plate.Name}.");

            var recipe = hatch.Serve(plate, out var rejection);
            if (recipe == null) return ToolResult.Fail($"Not served, you still hold the {plate.Name}. {rejection}");

            Hands.Release();
            counter.PutClean();
            return ToolResult.Ok($"You served a {recipe.Name}! A clean plate is back on the counter.");
        }
    }
}
