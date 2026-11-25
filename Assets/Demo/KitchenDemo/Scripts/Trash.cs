namespace Demo.KitchenDemo
{
    /// <summary>Gets rid of burnt food and mistakes.</summary>
    public class Trash : Station
    {
        public override string Status => "";
        protected override string LabelText => StationName;
    }
}
