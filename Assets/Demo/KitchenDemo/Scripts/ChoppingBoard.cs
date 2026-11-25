using System.Threading;
using UnityEngine;

namespace Demo.KitchenDemo
{
    /// <summary>Chops one raw ingredient at a time. Takes game time (pauses with the game).</summary>
    public class ChoppingBoard : Station
    {
        [SerializeField, Min(0.1f)] private float chopSeconds = 2f;

        private Food _chopping;

        public override string Status => _chopping == null ? "free" : $"chopping {_chopping.Name}";

        public async Awaitable Chop(Food food, CancellationToken ct)
        {
            _chopping = food;
            try
            {
                await Awaitable.WaitForSecondsAsync(chopSeconds, ct);
                food.State = FoodState.Chopped;
            }
            finally
            {
                _chopping = null;
            }
        }
    }
}
