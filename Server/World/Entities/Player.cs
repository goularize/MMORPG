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

        public void AddExp(long amount)
        {
            if (amount <= 0) return;

            Exp += amount;
            Console.WriteLine($"[Progression] {Name} gained {amount} EXP! (Total: {Exp})");

            bool leveledUp = false;
            long requiredExp = ServerConfig.GetExpForNextLevel(Level);

            while (Exp >= requiredExp)
            {
                Exp -= requiredExp;
                Level++;
                StatPoints += ServerConfig.StatPointsPerLevel;
                leveledUp = true;

                // Recalculate derived attributes for the new level
                CalculateDerivedStats();

                // Fully restore vitals on level-up
                Health = MaxHealth;
                Mana = MaxMana;

                Console.WriteLine($"[Level Up] {Name} reached Level {Level}! Gained {ServerConfig.StatPointsPerLevel} stat points (Total: {StatPoints}).");

                // Broadcast LevelUp to map AoI
                var map = GameLogic.MapMgr.GetMap(MapId);
                if (map != null)
                {
                    using Shared.Network.Packet levelUpPacket = new Shared.Network.Packet(Shared.Network.OpCode.PlayerLevelUp);
                    levelUpPacket.Write(Id);
                    levelUpPacket.Write(Level);
                    levelUpPacket.Write(StatPoints);
                    map.Broadcast(levelUpPacket, Position);
                }

                requiredExp = ServerConfig.GetExpForNextLevel(Level);
            }

            // Sync updated EXP status to player
            using Shared.Network.Packet expPacket = new Shared.Network.Packet(Shared.Network.OpCode.PlayerExpUpdate);
            expPacket.Write(Id);
            expPacket.Write(Exp);
            expPacket.Write(requiredExp);
            Connection.Send(expPacket);

            if (leveledUp)
            {
                // Sync updated Stats to player
                using Shared.Network.Packet statsPacket = new Shared.Network.Packet(Shared.Network.OpCode.StatsUpdate);
                statsPacket.Write(MaxHealth);
                statsPacket.Write(MaxMana);
                statsPacket.Write(Attack);
                statsPacket.Write(MagicAttack);
                statsPacket.Write(Defense);
                statsPacket.Write(MagicDefense);
                Connection.Send(statsPacket);

                // Sync full Vitals to player
                using Shared.Network.Packet vitalsPacket = new Shared.Network.Packet(Shared.Network.OpCode.VitalsUpdate);
                vitalsPacket.Write(Id);
                vitalsPacket.Write(Health);
                vitalsPacket.Write(Mana);
                Connection.Send(vitalsPacket);
            }

            // Asynchronously persist progression to database
            SaveProgressionToDatabase();
        }

        public void SaveProgressionToDatabase()
        {
            int playerId = Id;
            int level = Level;
            long exp = Exp;
            int statPoints = StatPoints;
            int health = Health;
            int mana = Mana;
            int strength = Strength;
            int intelligence = Intelligence;
            int constitution = Constitution;
            int knowledge = Knowledge;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using var db = Server.Database.AppDbContext.Factory();
                    var dbChar = db.Characters.Find(playerId);
                    if (dbChar != null)
                    {
                        dbChar.Level = level;
                        dbChar.Exp = exp;
                        dbChar.StatPoints = statPoints;
                        dbChar.Health = health;
                        dbChar.Mana = mana;
                        dbChar.Strength = strength;
                        dbChar.Intelligence = intelligence;
                        dbChar.Constitution = constitution;
                        dbChar.Knowledge = knowledge;
                        db.SaveChanges();
                    }
                }
                catch (System.Exception ex)
                {
                    System.Console.WriteLine($"[Error] Failed to persist progression for player ID {playerId}: {ex.Message}");
                }
            });
        }
    }
}
