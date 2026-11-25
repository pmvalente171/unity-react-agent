using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Demo.KitchenDemo
{
    public enum Ingredient
    {
        Tomato,
        Lettuce,
        Bread,
        Meat,
    }

    public enum FoodState
    {
        Raw,
        Chopped,
        Cooked,
        Burnt,
    }

    /// <summary>Anything the character can carry: a <see cref="Food"/> or a <see cref="Plate"/>.</summary>
    public interface IHoldable
    {
        string Name { get; }
        Color Color { get; }
    }

    public sealed class Food : IHoldable
    {
        public Ingredient Ingredient { get; }
        public FoodState State { get; set; }

        public Food(Ingredient ingredient, FoodState state = FoodState.Raw)
        {
            Ingredient = ingredient;
            State = state;
        }

        public bool CanChop => State == FoodState.Raw && Ingredient != Ingredient.Bread;
        public bool CanCook => State == FoodState.Chopped && Ingredient == Ingredient.Meat;

        public string Name => NameOf(Ingredient, State);

        public static string NameOf(Ingredient ingredient, FoodState state) =>
            ingredient == Ingredient.Bread && state == FoodState.Raw
                ? "bread"
                : $"{state.ToString().ToLowerInvariant()} {ingredient.ToString().ToLowerInvariant()}";

        public Color Color
        {
            get
            {
                if (State == FoodState.Burnt) return new Color(0.1f, 0.08f, 0.08f);
                if (State == FoodState.Cooked) return new Color(0.45f, 0.25f, 0.12f);

                var color = Ingredient switch
                {
                    Ingredient.Tomato => new Color(0.9f, 0.15f, 0.1f),
                    Ingredient.Lettuce => new Color(0.35f, 0.8f, 0.25f),
                    Ingredient.Bread => new Color(0.9f, 0.75f, 0.45f),
                    _ => new Color(0.85f, 0.4f, 0.45f),
                };
                return State == FoodState.Chopped ? Color.Lerp(color, Color.white, 0.3f) : color;
            }
        }
    }

    public sealed class Plate : IHoldable
    {
        public List<Food> Contents { get; } = new();

        public string Name => Contents.Count == 0
            ? "empty plate"
            : "plate with " + string.Join(", ", Contents.Select(f => f.Name));

        public Color Color => new(0.95f, 0.95f, 0.95f);
    }

    /// <summary>A dish: the exact set of food that must be on the plate, in any order.</summary>
    public sealed class Recipe
    {
        public string Name { get; }
        public string Hint { get; }
        private readonly (Ingredient Ingredient, FoodState State)[] _parts;

        public Recipe(string name, string hint, params (Ingredient, FoodState)[] parts)
        {
            Name = name;
            Hint = hint;
            _parts = parts;
        }

        public bool Matches(Plate plate) =>
            _parts.OrderBy(p => p.Ingredient).ThenBy(p => p.State)
                .SequenceEqual(plate.Contents.Select(f => (f.Ingredient, f.State))
                    .OrderBy(p => p.Ingredient).ThenBy(p => p.State));

        public string Describe()
        {
            var parts = string.Join(" + ", _parts.Select(p => Food.NameOf(p.Ingredient, p.State)));
            return string.IsNullOrEmpty(Hint) ? $"{Name}: {parts}" : $"{Name}: {parts} ({Hint})";
        }
    }

    public static class Recipes
    {
        public static readonly Recipe[] All =
        {
            new("salad", "chop both",
                (Ingredient.Lettuce, FoodState.Chopped), (Ingredient.Tomato, FoodState.Chopped)),
            new("burger", "chop the meat, then cook it",
                (Ingredient.Bread, FoodState.Raw), (Ingredient.Meat, FoodState.Cooked)),
        };

        public static Recipe Match(Plate plate) => All.FirstOrDefault(r => r.Matches(plate));

        public static string DescribeAll() => string.Join("\n", All.Select(r => "- " + r.Describe()));
    }
}
