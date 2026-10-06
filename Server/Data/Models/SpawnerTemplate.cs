namespace Server.Data.Models
{
    public class SpawnerTemplate
    {
        public string Type { get; set; } = "Point";
        public int TemplateId { get; set; }
        // Map this spawner belongs to. Defaults to 1 so legacy Spawners.json files without the field keep working.
        public int MapId { get; set; } = 1;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Radius { get; set; }
        public int Amount { get; set; }
        public float RotationY { get; set; }
    }
}
