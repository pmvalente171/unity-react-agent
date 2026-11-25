using UnityEngine;

namespace Demo.KitchenDemo
{
    /// <summary>
    /// Cooks one chopped ingredient on its own: cooked after <c>cookSeconds</c>, burnt if left
    /// <c>burnSeconds</c> longer. Uses game time, so it pauses with the game.
    /// </summary>
    public class Stove : Station
    {
        [SerializeField, Min(0.1f)] private float cookSeconds = 6f;
        [SerializeField, Min(0.1f)] private float burnSeconds = 10f;

        private float _elapsed;

        public Food Food { get; private set; }
        public bool IsEmpty => Food == null;
        public bool IsCooking => Food is { State: FoodState.Chopped };
        public float SecondsUntilCooked => Mathf.Max(0f, cookSeconds - _elapsed);
        public float SecondsUntilBurnt => Mathf.Max(0f, cookSeconds + burnSeconds - _elapsed);

        public override string Status
        {
            get
            {
                if (Food == null) return "empty";
                return Food.State switch
                {
                    FoodState.Chopped => $"cooking {Food.Name}, ready in {SecondsUntilCooked:F0} s",
                    FoodState.Cooked => $"{Food.Name} is ready, burns in {SecondsUntilBurnt:F0} s",
                    _ => $"{Food.Name} (throw it away)",
                };
            }
        }

        public void Put(Food food)
        {
            Food = food;
            _elapsed = 0f;
        }

        public Food Take()
        {
            var food = Food;
            Food = null;
            return food;
        }

        private void Update()
        {
            if (Food == null || Food.State == FoodState.Burnt) return;

            _elapsed += Time.deltaTime;
            if (Food.State == FoodState.Chopped && _elapsed >= cookSeconds) Food.State = FoodState.Cooked;
            if (Food.State == FoodState.Cooked && _elapsed >= cookSeconds + burnSeconds) Food.State = FoodState.Burnt;
        }
    }
}
