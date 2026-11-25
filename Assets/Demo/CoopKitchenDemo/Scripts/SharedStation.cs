using Demo.KitchenDemo;

namespace Demo.CoopKitchenDemo
{
    /// <summary>A station in the middle zone that both cooks use. Remembers who changed it last.</summary>
    public abstract class SharedStation : Station
    {
        public int Version { get; private set; }
        public string LastChangedBy { get; private set; }

        protected void MarkChanged(string by)
        {
            Version++;
            LastChangedBy = by;
        }
    }
}
