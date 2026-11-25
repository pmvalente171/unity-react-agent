using System.ComponentModel;
using System.Text;
using System.Threading;
using AI.Tools;
using Demo.Shared;
using UnityEngine;

namespace Demo.KitchenDemo
{
    /// <summary>
    /// The cook's tools, as [AgentTool] methods. Every tool walks to its station by itself, so one
    /// agent step does one meaningful kitchen action. Stations are found through <see cref="Station"/>.
    /// </summary>
    [RequireComponent(typeof(DemoCharacter), typeof(Hands))]
    public class KitchenTools : MonoBehaviour
    {
        [SerializeField] private ToolSet toolSet;

        private DemoCharacter _character;
        private Hands _hands;

        private void Awake()
        {
            _character = GetComponent<DemoCharacter>();
            _hands = GetComponent<Hands>();
        }

        private void OnEnable() => toolSet.RegisterFrom(this);
        private void OnDisable() => toolSet.UnregisterFrom(this);

        [AgentTool, Description("Check the kitchen: what you are holding, what every station is doing, and the recipes. " +
                                "Cheap and instant; use it whenever you are unsure.")]
        private string CheckKitchen()
        {
            var text = new StringBuilder();
            text.AppendLine($"You are holding: {_hands.Describe()}.");
            text.AppendLine("Stations:");
            foreach (var station in Station.All)
            {
                var status = station.Status;
                text.AppendLine(string.IsNullOrEmpty(status) ? $"- {station.StationName}" : $"- {station.StationName}: {status}");
            }

            if (KitchenDirector.Current != null)
                text.AppendLine("Orders (oldest first):\n" + KitchenDirector.Current.DescribeOrders());

            text.AppendLine("Recipes (everything on one plate, then serve the plate):");
            text.Append(Recipes.DescribeAll());
            return text.ToString();
        }

        [AgentTool, Description("Walk to an ingredient crate and take one raw ingredient. Your hands must be empty.")]
        private async Awaitable<ToolResult> TakeIngredient(
            [Description("Which ingredient to take.")] Ingredient ingredient,
            CancellationToken ct)
        {
            if (!_hands.IsEmpty) return HandsFull();

            var crate = Crate.For(ingredient);
            if (crate == null) return ToolResult.Fail($"There is no {ingredient} crate.");

            var error = await WalkTo(crate, ct);
            if (error != null) return ToolResult.Fail(error);

            var food = crate.Take();
            _hands.Hold(food);
            return ToolResult.Ok($"You took {food.Name}.");
        }

        [AgentTool, Description("Walk to the chopping board and chop the raw ingredient you are holding " +
                                "(not bread). Takes a few seconds; you keep holding it.")]
        private async Awaitable<ToolResult> Chop(CancellationToken ct)
        {
            if (_hands.Held is not Food food) return ToolResult.Fail($"You need to hold a raw ingredient (you hold {_hands.Describe()}).");
            if (!food.CanChop) return ToolResult.Fail($"{food.Name} can't be chopped.");

            var board = Station.Find<ChoppingBoard>();
            var error = await WalkTo(board, ct);
            if (error != null) return ToolResult.Fail(error);

            await board.Chop(food, ct);
            _hands.Refresh();
            return ToolResult.Ok($"You now hold {food.Name}.");
        }

        [AgentTool, Description("Walk to the stove and start cooking the chopped meat you are holding. " +
                                "Returns at once: the food keeps cooking while you do other things, " +
                                "and burns if you leave it too long.")]
        private async Awaitable<ToolResult> Cook(CancellationToken ct)
        {
            if (_hands.Held is not Food food || !food.CanCook)
                return ToolResult.Fail($"Only chopped meat can be cooked (you hold {_hands.Describe()}).");

            var stove = Station.Find<Stove>();
            if (!stove.IsEmpty) return ToolResult.Fail($"The stove is busy: {stove.Status}.");

            var error = await WalkTo(stove, ct);
            if (error != null) return ToolResult.Fail(error);

            stove.Put((Food)_hands.Release());
            return ToolResult.Ok($"The {food.Name} is cooking: ready in {stove.SecondsUntilCooked:F0} s, " +
                                 $"burnt after {stove.SecondsUntilBurnt:F0} s. Your hands are free.");
        }

        [AgentTool, Description("Walk to the stove and wait there until the food on it is cooked.")]
        private async Awaitable<ToolResult> WaitForStove(CancellationToken ct)
        {
            var stove = Station.Find<Stove>();
            if (stove.IsEmpty) return ToolResult.Fail("Nothing is on the stove.");

            var error = await WalkTo(stove, ct);
            if (error != null) return ToolResult.Fail(error);

            while (stove.IsCooking)
                await Awaitable.NextFrameAsync(ct);

            return ToolResult.Ok($"Stove: {stove.Status}.");
        }

        [AgentTool, Description("Walk to the stove and take the food off it. Your hands must be empty.")]
        private async Awaitable<ToolResult> TakeFromStove(CancellationToken ct)
        {
            if (!_hands.IsEmpty) return HandsFull();

            var stove = Station.Find<Stove>();
            if (stove.IsEmpty) return ToolResult.Fail("Nothing is on the stove.");
            if (stove.IsCooking)
                return ToolResult.Fail($"It's not cooked yet ({stove.Status}). Do something else or use wait_for_stove.");

            var error = await WalkTo(stove, ct);
            if (error != null) return ToolResult.Fail(error);

            var food = stove.Take();
            _hands.Hold(food);
            return food.State == FoodState.Burnt
                ? ToolResult.Ok($"You took {food.Name}. It's burnt: throw it away.")
                : ToolResult.Ok($"You took {food.Name}.");
        }

        [AgentTool, Description("Walk to the plate counter and put the food you are holding on the plate.")]
        private async Awaitable<ToolResult> PutOnPlate(CancellationToken ct)
        {
            if (_hands.Held is not Food food) return ToolResult.Fail($"You need to hold food (you hold {_hands.Describe()}).");
            if (food.State == FoodState.Burnt) return ToolResult.Fail($"The {food.Name} is burnt. Throw it away.");

            var counter = Station.Find<PlateCounter>();
            if (counter.Plate == null) return ToolResult.Fail("There is no plate on the counter.");

            var error = await WalkTo(counter, ct);
            if (error != null) return ToolResult.Fail(error);

            counter.Plate.Contents.Add((Food)_hands.Release());
            return ToolResult.Ok($"The counter now has a {counter.Plate.Name}.");
        }

        [AgentTool, Description("Walk to the plate counter and pick up the plate, ready to serve. Your hands must be empty.")]
        private async Awaitable<ToolResult> TakePlate(CancellationToken ct)
        {
            if (!_hands.IsEmpty) return HandsFull();

            var counter = Station.Find<PlateCounter>();
            if (counter.Plate == null) return ToolResult.Fail("There is no plate on the counter.");

            var error = await WalkTo(counter, ct);
            if (error != null) return ToolResult.Fail(error);

            var plate = counter.TakePlate();
            _hands.Hold(plate);
            return ToolResult.Ok($"You hold a {plate.Name}.");
        }

        [AgentTool, Description("Walk to the serving hatch and serve the plate you are holding. " +
                                "It must match a recipe exactly.")]
        private async Awaitable<ToolResult> Serve(CancellationToken ct)
        {
            if (_hands.Held is not Plate plate) return ToolResult.Fail($"You need to hold a plate (you hold {_hands.Describe()}).");

            var hatch = Station.Find<ServingHatch>();
            var error = await WalkTo(hatch, ct);
            if (error != null) return ToolResult.Fail(error);

            var recipe = hatch.Serve(plate, out var rejection);
            if (recipe == null)
                return ToolResult.Fail($"Not served, you still hold the {plate.Name}. {rejection}");

            _hands.Release();
            Station.Find<PlateCounter>().PutClean();
            return ToolResult.Ok($"You served a {recipe.Name}! A clean plate is back on the counter.");
        }

        [AgentTool, Description("Walk to the trash and throw away what you are holding. " +
                                "A plate is emptied and put back on the counter.")]
        private async Awaitable<ToolResult> ThrowAway(CancellationToken ct)
        {
            if (_hands.IsEmpty) return ToolResult.Fail("You aren't holding anything.");

            var error = await WalkTo(Station.Find<Trash>(), ct);
            if (error != null) return ToolResult.Fail(error);

            var item = _hands.Release();
            if (item is Plate) Station.Find<PlateCounter>().PutClean();
            return ToolResult.Ok(item is Plate ? "You emptied the plate; a clean one is on the counter." : $"You threw away the {item.Name}.");
        }

        // ---- Helpers ----

        private ToolResult HandsFull() =>
            ToolResult.Fail($"Your hands are full (you hold {_hands.Describe()}). " +
                            "Put it on the plate, cook it, serve it or throw it away first.");

        /// <summary>Walks to the station and faces it. Returns null on success, otherwise why it failed.</summary>
        private async Awaitable<string> WalkTo(Station station, CancellationToken ct)
        {
            if (station == null) return "That station doesn't exist in this kitchen.";

            var result = await _character.MoveTo(station.StandPoint, ct);
            if (result != MoveResult.Arrived)
                return result == MoveResult.Unreachable
                    ? $"You can't reach the {station.StationName}."
                    : $"You couldn't get to the {station.StationName} in time.";

            var look = station.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(look);
            return null;
        }
    }
}
