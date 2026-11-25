using System.Collections.Generic;
using System.Linq;
using AI;
using Demo.KitchenDemo;
using Demo.Shared;
using UnityEngine;

namespace Demo.CoopKitchenDemo
{
    /// <summary>
    /// Runs the two-cook restaurant: spawns timed orders, scores served dishes, and drives every
    /// cook's agent. Whenever a cook is idle and orders are open, it gets one message with its role
    /// and the orders, so both agents work at the same time and coordinate through the plan board.
    /// </summary>
    public class CoopDirector : MonoBehaviour
    {
        private const string Sender = "Kitchen";

        [SerializeField, Tooltip("Leave empty to use every cook in the scene.")]
        private CoopCook[] cooks;

        [SerializeField] private bool runOnStart = true;
        [SerializeField, Min(1)] private int maxOpenOrders = 2;
        [SerializeField, Min(5f)] private float orderTimeLimit = 180f;
        [SerializeField, Min(1f)] private float secondsBetweenOrders = 60f;
        [SerializeField, Min(0f)] private float pauseBetweenRuns = 2f;
        [SerializeField, Min(1)] private int maxConsecutiveFailures = 3;
        [SerializeField] private int pointsPerDish = 10;
        [SerializeField] private int pointsPerExpiredOrder = -5;

        private sealed class Seat
        {
            public CoopCook Cook;
            public Agent Agent;
            public bool WasBusy;
            public float NextSendAt;
        }

        private readonly List<Order> _orders = new();
        private readonly List<Seat> _seats = new();
        private ServingHatch _hatch;
        private OrderBoardView _view;
        private int _nextId = 1;
        private float _spawnTimer;
        private float _holdUntil;
        private int _failures;
        private string _status = "";

        public static CoopDirector Current { get; private set; }

        public bool IsRunning { get; private set; }
        public int Score { get; private set; }
        public int ServedCount { get; private set; }

        public string DescribeOrders() =>
            _orders.Count == 0 ? "No open orders." : string.Join("\n", _orders.Select(o => "- " + o.Describe()));

        public void Run()
        {
            IsRunning = true;
            _failures = 0;
            _status = "";
            if (_orders.Count == 0) SpawnOrder();
        }

        public void Pause(string reason = "Paused.")
        {
            IsRunning = false;
            _status = reason;
        }

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this) Current = null;
            foreach (var seat in _seats)
            {
                if (seat.Agent == null) continue;
                seat.Agent.FinalAnswer -= OnFinalAnswer;
                seat.Agent.Failed -= OnFailed;
                seat.Agent.Cancelled -= OnCancelled;
            }

            _seats.Clear();
            if (_hatch != null)
            {
                _hatch.Served -= OnServed;
                _hatch.Validate = null;
            }

            _view?.Remove();
            _view = null;
        }

        private void Start()
        {
            if (cooks == null || cooks.Length == 0) cooks = FindObjectsByType<CoopCook>(FindObjectsSortMode.None);
            foreach (var cook in cooks)
            {
                var agent = cook.GetComponent<Agent>();
                if (agent == null) continue;
                agent.FinalAnswer += OnFinalAnswer;
                agent.Failed += OnFailed;
                agent.Cancelled += OnCancelled;
                _seats.Add(new Seat { Cook = cook, Agent = agent });
            }

            // Stations register in their OnEnable, so look them up here.
            _hatch = Station.Find<ServingHatch>();
            if (_seats.Count == 0 || _hatch == null)
            {
                Debug.LogError("[CoopDirector] Needs cooks with an Agent and a ServingHatch in the scene.", this);
                enabled = false;
                return;
            }

            _hatch.Validate = r => _orders.Any(o => o.Recipe == r) ? null : $"Nobody ordered a {r.Name}. Open orders:\n{DescribeOrders()}";
            _hatch.Served += OnServed;

            if (WorldOverlay.Current != null)
                _view = new OrderBoardView(WorldOverlay.Current.Root, () => { if (IsRunning) Pause(); else Run(); });

            if (runOnStart) Run();
            else _status = "Press Start to open the kitchen.";
        }

        private void Update()
        {
            if (IsRunning)
            {
                TickOrders(Time.deltaTime);
                foreach (var seat in _seats) TickSeat(seat);
            }

            _view?.Refresh($"Orders  ·  score {Score}  ·  served {ServedCount}", DescribeOrders(), _status, IsRunning);
        }

        private void TickSeat(Seat seat)
        {
            if (seat.Agent.IsBusy)
            {
                seat.WasBusy = true;
                return;
            }

            if (seat.WasBusy)
            {
                seat.WasBusy = false;
                seat.NextSendAt = Time.unscaledTime + pauseBetweenRuns;
            }

            if (_orders.Count > 0 && Time.unscaledTime >= seat.NextSendAt && Time.unscaledTime >= _holdUntil)
                Send(seat);
        }

        private void Send(Seat seat)
        {
            var partners = string.Join(" and ", _seats.Where(s => s != seat).Select(s => s.Cook.CookName));
            var prompt =
                $"You are the {seat.Cook.CookName} in a two-cook kitchen. {seat.Cook.Role}\n" +
                $"Your partner is the {partners}. You can't use the stations in their zone: hand food over " +
                "through the pass counter and plan together on the plan board in the middle.\n" +
                "Open orders, oldest first:\n" + DescribeOrders() + "\n" +
                "Help serve the oldest order. Start with check_kitchen. If the board has no plan for this order yet, " +
                "post a short one that splits the work; otherwise follow it. Post a note when you hand something over, " +
                "and use wait_for_partner when you need something from them. When your part is done, reply with one short sentence.";

            seat.Agent.ResetConversation(); // the plan board is the shared memory between runs
            _ = seat.Agent.Send(prompt, Sender);
        }

        private void TickOrders(float deltaTime)
        {
            for (var i = _orders.Count - 1; i >= 0; i--)
            {
                _orders[i].TimeLeft -= deltaTime;
                if (_orders[i].TimeLeft > 0f) continue;
                _orders.RemoveAt(i);
                Score += pointsPerExpiredOrder;
            }

            _spawnTimer += deltaTime;
            var due = _spawnTimer >= secondsBetweenOrders || (_orders.Count == 0 && _spawnTimer >= 3f);
            if (due && _orders.Count < maxOpenOrders) SpawnOrder();
        }

        private void SpawnOrder()
        {
            _orders.Add(new Order { Id = _nextId++, Recipe = Recipes.All[Random.Range(0, Recipes.All.Length)], TimeLeft = orderTimeLimit });
            _spawnTimer = 0f;
        }

        private void OnServed(Recipe recipe)
        {
            var order = _orders.FirstOrDefault(o => o.Recipe == recipe);
            if (order == null) return;
            _orders.Remove(order);
            Score += pointsPerDish;
            ServedCount++;
        }

        private void OnFinalAnswer(string _)
        {
            _failures = 0;
            _status = "";
        }

        private void OnFailed(string error)
        {
            _failures++;
            _holdUntil = Time.unscaledTime + 5f;
            if (_failures >= maxConsecutiveFailures) Pause($"Paused: agents failed {_failures} times in a row ({error}). Press Start to retry.");
            else _status = $"An agent failed ({error}); retrying in 5 s.";
        }

        // The player typed /stop: they're taking over, so stop sending orders until Start is pressed.
        private void OnCancelled() => Pause("Paused because an agent was stopped. Press Start to resume.");
    }
}
