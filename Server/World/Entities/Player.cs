using System.Collections.Generic;

namespace Server.World.Entities
{
    public class Player : Entity
    {
        // AoI Tracking
        public HashSet<int> KnownEntities { get; } = new();

        // Account Relationship
        public int AccountId { get; set; }
        public Server.Network.IClientConnection Connection { get; set; } // The active network session

        // RPG Progression
        public int Level { get; set; }
        public long Exp { get; set; }
        public int StatPoints { get; set; }

        public Player(int id, string name, Server.Network.IClientConnection connection)
        {
            Id = id;
            Name = name;
            Connection = connection;
        }

        public override void Update()
        {
            base.Update();
            // Process player-specific logic (e.g. processing queued inputs)
        }
    }
}
