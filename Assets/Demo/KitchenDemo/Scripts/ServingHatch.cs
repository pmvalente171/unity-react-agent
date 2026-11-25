using System;

namespace Demo.KitchenDemo
{
    /// <summary>Takes finished plates. <see cref="Served"/> is what the order director listens to.</summary>
    public class ServingHatch : Station
    {
        public int ServedCount { get; private set; }

        public event Action<Recipe> Served;

        /// <summary>
        /// Optional check before a dish is accepted (the director uses it to refuse dishes nobody ordered).
        /// Return null to accept, or the reason to refuse.
        /// </summary>
        public Func<Recipe, string> Validate { get; set; }

        public override string Status => ServedCount == 0 ? "" : $"{ServedCount} served";

        /// <summary>The recipe served, or null with <paramref name="rejection"/> set (the plate is then not taken).</summary>
        public Recipe Serve(Plate plate, out string rejection)
        {
            var recipe = Recipes.Match(plate);
            if (recipe == null)
            {
                rejection = $"A {plate.Name} isn't any recipe. Recipes:\n{Recipes.DescribeAll()}";
                return null;
            }

            rejection = Validate?.Invoke(recipe);
            if (rejection != null) return null;

            ServedCount++;
            Served?.Invoke(recipe);
            return recipe;
        }
    }
}
