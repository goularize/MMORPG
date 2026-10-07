namespace Server.Data.Models
{
    /// <summary>Places one resource node of a <see cref="ResourceNodeTemplate"/> on a map.</summary>
    public class ResourceSpawnTemplate
    {
        public int TemplateId { get; set; }
        public int MapId { get; set; } = 1;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }
}
