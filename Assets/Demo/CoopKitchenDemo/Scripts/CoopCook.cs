using System;
using System.ComponentModel;
using System.Text;
using System.Threading;
using AI.Tools;
using Demo.KitchenDemo;
using Demo.Shared;
using UnityEngine;

namespace Demo.CoopKitchenDemo
{
    /// <summary>
    /// Tools every cook has. A cook only reaches the stations under its own <c>zone</c> and the
    /// shared <c>middle</c> zone; the partner is reached only through the pass counter and plan board.
    /// <see cref="PrepCook"/> and <see cref="LineCook"/> add the tools for their zone.
    /// </summary>
    [RequireComponent(typeof(DemoCharacter), typeof(Hands))]
    public abstract class CoopCook : MonoBehaviour
    {
        [SerializeField] private ToolSet toolSet;
        [SerializeField] private string cookName = "cook";
        [SerializeField, TextArea] private string role = "";
        [SerializeField, Tooltip("Parent of this cook's own stations.")] private Transform zone;
        [SerializeField, Tooltip("Parent of the stations both cooks share.")] private Transform middle;
        [SerializeField, Tooltip("Where to stand at a shared station, from its centre (the side facing your zone).")]
        private Vector3 sharedStandOffset = new(-1.2f, 0f, 0f);
        [SerializeField, Min(1f)] private float waitTimeoutSeconds = 30f;

        protected DemoCharacter Character { get; private set; }
        protected Hands Hands { get; private set; }

        public string CookName => cookName;
        public string Role => role;

        private void Awake()
        {
            Character = GetComponent<DemoCharacter>();
            Hands = GetComponent<Hands>();
        }

        private void OnEnable() => toolSet.RegisterFrom(this);
        private void OnDisable() => toolSet.UnregisterFrom(this);

        [AgentTool, Description("Check the kitchen: what you hold, your stations, the pass counter, the plan board, " +
                                "the orders and the recipes. Cheap and instant.")]
        private string CheckKitchen()
        {
            var text = new StringBuilder();
            text.AppendLine($"You are holding: {Hands.Describe()}.");
            text.AppendLine("Stations you can use:");
            var partner = new StringBuilder();
            foreach (var station in Station.All)
            {
                if (!InReach(station))
                {
                    partner.Append(partner.Length == 0 ? station.StationName : ", " + station.StationName);
                    continue;
                }

                var status = station.Status;
                text.AppendLine(string.IsNullOrEmpty(status) ? $"- {station.StationName}" : $"- {station.StationName}: {status}");
            }

            text.AppendLine($"Only your partner can use: {partner}.");
            text.AppendLine(Find<PlanBoard>()?.Describe());
            if (CoopDirector.Current != null) text.AppendLine("Orders (oldest first):\n" + CoopDirector.Current.DescribeOrders());
            text.AppendLine("Recipes (everything on one plate, then serve the plate):");
            text.Append(Recipes.DescribeAll());
            return text.ToString();
        }

        [AgentTool, Description("Walk to a crate in your zone and take one raw ingredient. Your hands must be empty.")]
        private async Awaitable<ToolResult> TakeIngredient([Description("Which ingredient.")] Ingredient ingredient, CancellationToken ct)
        {
            if (!Hands.IsEmpty) return HandsFull();

            var crate = Find<Crate>(c => c.Ingredient == ingredient);
            if (crate == null) return ToolResult.Fail($"There is no {ingredient} crate in your zone. Ask your partner on the plan board.");

            if (await WalkTo(crate, ct) is { } error) return ToolResult.Fail(error);
            Hands.Hold(crate.Take());
            return ToolResult.Ok($"You took {Hands.Describe()}.");
        }

        [AgentTool, Description("Walk to the pass counter in the middle and leave what you hold there for your partner.")]
        private async Awaitable<ToolResult> PutOnPass(CancellationToken ct)
        {
            if (Hands.IsEmpty) return ToolResult.Fail("You aren't holding anything.");

            var pass = Find<PassCounter>();
            if (pass.IsFull) return ToolResult.Fail($"The pass counter is full: {pass.Status}.");

            if (await WalkTo(pass, ct) is { } error) return ToolResult.Fail(error);
            pass.Put(Hands.Release(), cookName);
            return ToolResult.Ok($"Pass counter: {pass.Status}.");
        }

        [AgentTool, Description("Walk to the pass counter and take one item from it. Your hands must be empty.")]
        private async Awaitable<ToolResult> TakeFromPass([Description("The item's exact name, e.g. \"chopped tomato\".")] string item,
            CancellationToken ct)
        {
            if (!Hands.IsEmpty) return HandsFull();

            var pass = Find<PassCounter>();
            if (await WalkTo(pass, ct) is { } error) return ToolResult.Fail(error);

            var taken = pass.Take(item, cookName);
            if (taken == null) return ToolResult.Fail($"There is no '{item}' on the pass counter ({pass.Status}).");
            Hands.Hold(taken);
            return ToolResult.Ok($"You took {taken.Name}.");
        }

        [AgentTool, Description("Walk to the plan board in the middle and post a short note for your partner " +
                                "(a plan, a request, or what you just did).")]
        private async Awaitable<ToolResult> PostNote([Description("The note, one or two sentences.")] string note, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(note)) return ToolResult.Fail("The note is empty.");

            var board = Find<PlanBoard>();
            if (await WalkTo(board, ct) is { } error) return ToolResult.Fail(error);
            board.Post(cookName, note);
            Character.Say(note);
            return ToolResult.Ok("Posted. " + board.Describe());
        }

        [AgentTool, Description("Wait until your partner posts a note or changes the pass counter, or 30 s pass. " +
                                "Use it when you need something from them.")]
        private async Awaitable<string> WaitForPartner(CancellationToken ct)
        {
            var board = Find<PlanBoard>();
            var pass = Find<PassCounter>();
            var boardVersion = board.Version;
            var passVersion = pass.Version;

            var deadline = Time.time + waitTimeoutSeconds;
            while (Time.time < deadline)
            {
                if (ChangedByPartner(board, boardVersion) || ChangedByPartner(pass, passVersion))
                    return $"Your partner did something.\n{board.Describe()}\nPass counter: {pass.Status}.";
                await Awaitable.NextFrameAsync(ct);
            }

            return $"Nothing from your partner in {waitTimeoutSeconds:F0} s.\n{board.Describe()}\nPass counter: {pass.Status}.";
        }

        [AgentTool, Description("Walk to your trash and throw away what you hold. A plate is emptied and put back on the counter.")]
        private async Awaitable<ToolResult> ThrowAway(CancellationToken ct)
        {
            if (Hands.IsEmpty) return ToolResult.Fail("You aren't holding anything.");
            if (await WalkTo(Find<Trash>(), ct) is { } error) return ToolResult.Fail(error);

            var item = Hands.Release();
            if (item is Plate) Find<PlateCounter>()?.PutClean();
            return ToolResult.Ok($"You threw away the {item.Name}.");
        }

        // ---- Helpers for the subclasses ----

        protected bool InReach(Station station) =>
            (zone != null && station.transform.IsChildOf(zone)) || (middle != null && station.transform.IsChildOf(middle));

        /// <summary>The first station of type T this cook can reach (and that matches), or null.</summary>
        protected T Find<T>(Func<T, bool> match = null) where T : Station
        {
            foreach (var station in Station.All)
                if (station is T t && InReach(station) && (match == null || match(t)))
                    return t;
            return null;
        }

        protected ToolResult HandsFull() =>
            ToolResult.Fail($"Your hands are full (you hold {Hands.Describe()}). Use it, pass it or throw it away first.");

        /// <summary>Walks to the station and faces it. Null on success, otherwise why it failed.</summary>
        protected async Awaitable<string> WalkTo(Station station, CancellationToken ct)
        {
            if (station == null) return "There is no such station in your zone.";

            var target = station is SharedStation ? station.transform.position + sharedStandOffset : station.StandPoint;
            var result = await Character.MoveTo(target, ct);
            if (result != MoveResult.Arrived)
                return result == MoveResult.Unreachable
                    ? $"You can't reach the {station.StationName}."
                    : $"You couldn't get to the {station.StationName} in time.";

            var look = station.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(look);
            return null;
        }

        private bool ChangedByPartner(SharedStation station, int version) =>
            station.Version != version && station.LastChangedBy != cookName;
    }
}
