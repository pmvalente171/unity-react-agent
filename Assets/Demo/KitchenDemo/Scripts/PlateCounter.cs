namespace Demo.KitchenDemo
{
    /// <summary>Where dishes are assembled. Holds one plate; a clean one appears after serving.</summary>
    public class PlateCounter : Station
    {
        public Plate Plate { get; private set; } = new();

        public override string Status => Plate == null ? "no plate (you have it)" : Plate.Name;

        public Plate TakePlate()
        {
            var plate = Plate;
            Plate = null;
            return plate;
        }

        public void PutClean() => Plate = new Plate();
    }
}
