namespace Server.Data.Models
{
    public class SpawnerTemplate
    {
        public string Type { get; set; } = "Point";
        public int TemplateId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Radius { get; set; }
        public int Amount { get; set; }
        public float RotationY { get; set; }
    }
}
