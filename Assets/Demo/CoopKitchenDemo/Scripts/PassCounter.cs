using System;
using System.Collections.Generic;
using System.Linq;
using Demo.KitchenDemo;
using UnityEngine;

namespace Demo.CoopKitchenDemo
{
    /// <summary>Counter in the middle where the cooks leave food (or plates) for each other.</summary>
    public class PassCounter : SharedStation
    {
        [SerializeField, Min(1)] private int capacity = 4;

        private readonly List<IHoldable> _items = new();

        public bool IsFull => _items.Count >= capacity;
        public override string Status => _items.Count == 0 ? "empty" : string.Join(", ", _items.Select(i => i.Name));

        public void Put(IHoldable item, string by)
        {
            _items.Add(item);
            MarkChanged(by);
        }

        /// <summary>Takes the item called <paramref name="itemName"/> (e.g. "chopped tomato"), or null.</summary>
        public IHoldable Take(string itemName, string by)
        {
            var index = _items.FindIndex(i => string.Equals(i.Name, itemName?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index < 0) return null;

            var item = _items[index];
            _items.RemoveAt(index);
            MarkChanged(by);
            return item;
        }
    }
}
