namespace Server.World.Entities
{
    public class NPC : Entity
    {
        public int SpawnId { get; set; }
        // public AIState CurrentState { get; set; }

        public NPC(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public override void Update()
        {
            base.Update();
            // Process NPC-specific logic (e.g. AI behavior tree, aggro radius)
        }
    }
}
