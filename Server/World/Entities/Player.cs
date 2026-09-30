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
        public long Exp { get; set; }
        public int StatPoints { get; set; }
        
        // Movement tracking
        public System.DateTime LastMoveTime { get; set; } = System.DateTime.UtcNow;
        // Bind Location
        public int BindMapId { get; set; }
        public Shared.Math.Vector3 BindPosition { get; set; }

        public Player(int id, string name, Server.Network.IClientConnection connection)
        {
            Id = id;
            Name = name;
            Connection = connection;
        }

        public override void Update()
        {
            base.Update();
            
            // Sync vitals to client if they changed (e.g. from regen)
            if (_vitalsChanged)
            {
                using Shared.Network.Packet packet = new Shared.Network.Packet(Shared.Network.OpCode.VitalsUpdate);
                packet.Write(Id);
                packet.Write(Health);
                packet.Write(Mana);
                Connection.Send(packet);
                _vitalsChanged = false;
            }
        }
    }
}
