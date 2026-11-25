using System;
using System.Collections.Generic;
using System.Linq;
using AI;
using Demo.Shared;
using UnityEngine;

namespace Demo.KitchenDemo
{
    public sealed class Order
    {
        public int Id;
        public Recipe Recipe;
        public float TimeLeft;

        public string Describe() => $"#{Id} {Recipe.Name}, {Mathf.CeilToInt(TimeLeft)} s left";
    }

    /// <summary>
    /// Runs the restaurant: creates orders over time, expires late ones, scores served dishes, and
    /// drives the agent. Whenever the agent is idle and orders are open, it sends one message asking
    /// for the oldest order, so each order is one agent run. The game drives the agent, not the chat.
    /// Order timers use game time, so they pause with the game.
    /// </summary>
    public class KitchenDirector : MonoBehaviour
    {
        private const string Sender = "Kitchen";

        [SerializeField, Tooltip("Leave empty to use the only Agent in the scene.")]
        private Agent agent;

        [SerializeField] private bool runOnStart = true;
        [SerializeField, Min(1)] private int maxOpenOrders = 3;
        [SerializeField, Min(5f)] private float orderTimeLimit = 120f;
        [SerializeField, Min(1f)] private float secondsBetweenOrders = 45f;

        [SerializeField, Tooltip("Start every order with an empty conversation: shorter prompts, faster steps. " +
                                 "The agent re-reads the kitchen with check_kitchen anyway.")]
        private bool resetHistoryPerOrder = true;

        [SerializeField, Min(1)] private int maxConsecutiveFailures = 3;

        [Header("Scoring")]
        [SerializeField] private int pointsPerDish = 10;
        [SerializeField, Tooltip("Bonus point per this many seconds left on the order.")]
        private float secondsPerBonusPoint = 10f;
        [SerializeField] private int pointsPerExpiredOrder = -5;

        private readonly List<Order> _orders = new();
        private ServingHatch _hatch;
        private OrderBoardView _view;
        private int _nextId = 1;
        private float _spawnTimer;
        private float _nextSendAt;
        private int _failures;
        private string _status = "";

        public static KitchenDirector Current { get; private set; }

        public IReadOnlyList<Order> Orders => _orders;
        public bool IsRunning { get; private set; }
        public int Score { get; private set; }
        public int CompletedCount { get; private set; }
        public int ExpiredCount { get; private set; }

        public event Action<Order> OrderAdded;
        public event Action<Order> OrderCompleted;
        public event Action<Order> OrderExpired;

        /// <summary>Starts (or resumes) taking orders and sending them to the agent.</summary>
        public void Run()
        {
            IsRunning = true;
            _failures = 0;
            _status = "";
            if (_orders.Count == 0) SpawnOrder();
            _nextSendAt = Time.unscaledTime + 1f;
        }

        /// <summary>Stops new orders, timers and messages. The agent finishes what it's doing.</summary>
        public void Pause(string reason = "Paused.")
        {
            IsRunning = false;
            _status = reason;
        }

        public string DescribeOrders() =>
            _orders.Count == 0 ? "No open orders." : string.Join("\n", _orders.Select(o => "- " + o.Describe()));

        private void OnEnable()
        {
            Current = this;
            if (agent == null) agent = FindAnyObjectByType<Agent>();
            if (agent == null)
            {
                Debug.LogError("[KitchenDirector] No Agent in the scene.", this);
                enabled = false;
                return;
            }

            agent.FinalAnswer += OnFinalAnswer;
            agent.Failed += OnFailed;
            agent.Cancelled += OnCancelled;
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
            if (agent != null)
            {
                agent.FinalAnswer -= OnFinalAnswer;
                agent.Failed -= OnFailed;
                agent.Cancelled -= OnCancelled;
            }

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
            // Stations register in their OnEnable, so look them up here, not in OnEnable.
            _hatch = Station.Find<ServingHatch>();
            if (_hatch == null)
            {
                Debug.LogError("[KitchenDirector] No ServingHatch in the scene.", this);
                enabled = false;
                return;
            }

            _hatch.Validate = ValidateServe;
            _hatch.Served += OnServed;

            if (WorldOverlay.Current != null)
                _view = new OrderBoardView(WorldOverlay.Current.Root, ToggleRunning);

            if (runOnStart) Run();
            else _status = "Press Start to open the kitchen.";
        }

        private void Update()
        {
            if (IsRunning)
            {
                TickOrders(Time.deltaTime);
                if (!agent.IsBusy && _orders.Count > 0 && Time.unscaledTime >= _nextSendAt)
                    SendNextOrder();
            }

            _view?.Refresh($"Orders  ·  score {Score}  ·  served {CompletedCount}  ·  missed {ExpiredCount}",
                DescribeOrders(), _status, IsRunning);
        }

        private void TickOrders(float deltaTime)
        {
            for (var i = _orders.Count - 1; i >= 0; i--)
            {
                var order = _orders[i];
                order.TimeLeft -= deltaTime;
                if (order.TimeLeft > 0f) continue;

                _orders.RemoveAt(i);
                Score += pointsPerExpiredOrder;
                ExpiredCount++;
                OrderExpired?.Invoke(order);
            }

            _spawnTimer += deltaTime;
            var due = _spawnTimer >= secondsBetweenOrders || (_orders.Count == 0 && _spawnTimer >= 3f);
            if (due && _orders.Count < maxOpenOrders) SpawnOrder();
        }

        private void SpawnOrder()
        {
            var order = new Order
            {
                Id = _nextId++,
                Recipe = Recipes.All[UnityEngine.Random.Range(0, Recipes.All.Length)],
                TimeLeft = orderTimeLimit,
            };
            _orders.Add(order);
            _spawnTimer = 0f;
            OrderAdded?.Invoke(order);
        }

        private void SendNextOrder()
        {
            var order = _orders[0];
            if (resetHistoryPerOrder) agent.ResetConversation();

            var prompt =
                "New task from the kitchen. Open orders, oldest first:\n" +
                DescribeOrders() + "\n" +
                $"Make and serve order #{order.Id} (a {order.Recipe.Name}). Start with check_kitchen: there may " +
                "already be food on the plate, the stove or in your hands. Plan so food doesn't burn. " +
                "When it's served, reply with one short sentence.";

            _status = $"Agent is working on order #{order.Id}.";
            _nextSendAt = float.MaxValue; // set again when this run ends
            _ = agent.Send(prompt, Sender);
        }

        private string ValidateServe(Recipe recipe) =>
            _orders.Any(o => o.Recipe == recipe)
                ? null
                : $"Nobody ordered a {recipe.Name}. Open orders:\n{DescribeOrders()}";

        private void OnServed(Recipe recipe)
        {
            var order = _orders.FirstOrDefault(o => o.Recipe == recipe);
            if (order == null) return; // ValidateServe prevents this

            _orders.Remove(order);
            Score += pointsPerDish + Mathf.FloorToInt(order.TimeLeft / secondsPerBonusPoint);
            CompletedCount++;
            OrderCompleted?.Invoke(order);
        }

        private void OnFinalAnswer(string _)
        {
            _failures = 0;
            _status = "";
            _nextSendAt = Time.unscaledTime + 1.5f;
        }

        private void OnFailed(string error)
        {
            _failures++;
            _nextSendAt = Time.unscaledTime + 5f;
            if (_failures >= maxConsecutiveFailures)
                Pause($"Paused: the agent failed {_failures} times in a row ({error}). Press Start to retry.");
            else
                _status = $"The agent failed ({error}); retrying in 5 s.";
        }

        // The player typed /stop: they're taking over, so stop sending orders until Start is pressed.
        private void OnCancelled() => Pause("Paused because the agent was stopped. Press Start to resume.");

        private void ToggleRunning()
        {
            if (IsRunning) Pause();
            else Run();
        }
    }
}
