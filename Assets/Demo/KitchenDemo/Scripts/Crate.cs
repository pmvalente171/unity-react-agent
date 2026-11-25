using UnityEngine;

namespace Demo.KitchenDemo
{
    /// <summary>Endless supply of one raw ingredient.</summary>
    public class Crate : Station
    {
        [SerializeField] private Ingredient ingredient;

        public Ingredient Ingredient => ingredient;
        public override string Status => "";
        protected override string LabelText => StationName;

        public Food Take() => new(ingredient);

        public static Crate For(Ingredient wanted)
        {
            foreach (var station in All)
                if (station is Crate crate && crate.ingredient == wanted)
                    return crate;
            return null;
        }
    }
}
