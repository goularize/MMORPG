using System.Collections.Generic;

namespace Server.World.Entities
{
    public class Player : Entity
    {
        public override Shared.Enums.EntityType Type => Shared.Enums.EntityType.Player;

        // AoI Tracking
        public HashSet<int> KnownEntities { get; } = new();

        // Account Relationship
        public int AccountId { get; set; }
        public Server.Network.IClientConnection Connection { get; set; } // The active network session

        // RPG Progression
        public long Exp { get; set; }
        public int StatPoints { get; set; }
        
        // Currency & Inventory
        public long Gold { get; set; } = 0;
        public int InventorySlots { get; set; } = 20;
        public Dictionary<Shared.Enums.EquipmentSlot, Server.Database.Models.CharacterItem> EquippedItems { get; } = new();
        public List<Server.Database.Models.CharacterItem> Inventory { get; } = new();
        public HashSet<int> LearnedRecipes { get; } = new();

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

        public override void CalculateDerivedStats()
        {
            // 1. Gather equipment stats
            int gearPhysicalAttack = 0;
            int gearMagicAttack = 0;
            int gearPhysicalDefense = 0;
            int gearMagicDefense = 0;
            int gearStr = 0;
            int gearInt = 0;
            int gearCon = 0;
            int gearKnow = 0;
            float gearCritChance = 0f;
            float gearCritMultiplier = 0f;
            float gearDodgeChance = 0f;
            float gearMoveSpeed = 0f;
            float gearAttackSpeed = 0f;

            foreach (var item in EquippedItems.Values)
            {
                float upgradeFactor = 1.0f + (item.UpgradeLevel * 0.05f);

                gearPhysicalAttack += (int)System.Math.Round(item.RolledPhysicalAttack * upgradeFactor);
                gearMagicAttack += (int)System.Math.Round(item.RolledMagicAttack * upgradeFactor);
                gearPhysicalDefense += (int)System.Math.Round(item.RolledPhysicalDefense * upgradeFactor);
                gearMagicDefense += (int)System.Math.Round(item.RolledMagicDefense * upgradeFactor);

                gearStr += (int)System.Math.Round(item.RolledStrength * upgradeFactor);
                gearInt += (int)System.Math.Round(item.RolledIntelligence * upgradeFactor);
                gearCon += (int)System.Math.Round(item.RolledConstitution * upgradeFactor);
                gearKnow += (int)System.Math.Round(item.RolledKnowledge * upgradeFactor);

                gearCritChance += item.RolledCritChance;
                gearCritMultiplier += item.RolledCritMultiplier;
                gearDodgeChance += item.RolledDodgeChance;
                gearMoveSpeed += item.RolledMovementSpeed;
                gearAttackSpeed += item.RolledAttackSpeedBonus;
            }

            int totalStr = Strength + gearStr;
            int totalInt = Intelligence + gearInt;
            int totalCon = Constitution + gearCon;
            int totalKnow = Knowledge + gearKnow;

            // 2. Vitals & Derived attributes
            MaxHealth = System.Math.Max(1, 50 + (totalCon * 10) + (Level * 15));
            MaxMana = System.Math.Max(1, 20 + (totalKnow * 5) + (Level * 8));

            // 3. Pillar 2: Core Attributes Multiplier on Weapon/Armor
            Attack = System.Math.Max(1, (int)System.Math.Round(10 + (totalStr * 2) + (Level * 1.5) + (gearPhysicalAttack * (1.0f + (totalStr / 100.0f)))));
            MagicAttack = System.Math.Max(1, (int)System.Math.Round(10 + (totalInt * 2) + (Level * 1.5) + (gearMagicAttack * (1.0f + (totalInt / 100.0f)))));
            Defense = System.Math.Max(0, (int)System.Math.Round((totalCon * 1.5) + (Level * 0.5) + (gearPhysicalDefense * (1.0f + (totalCon / 200.0f)))));
            MagicDefense = System.Math.Max(0, (int)System.Math.Round((totalKnow * 1.5) + (Level * 0.5) + (gearMagicDefense * (1.0f + (totalKnow / 200.0f)))));

            // 4. Secondary combat stats
            CritChance = System.Math.Clamp(0.05f + (totalInt * 0.0005f) + gearCritChance, 0.05f, 0.75f);
            CritMultiplier = System.Math.Max(1.5f, 2.0f + (totalStr * 0.005f) + gearCritMultiplier);
            DodgeChance = System.Math.Clamp(0.05f + (totalKnow * 0.0002f) + gearDodgeChance, 0.05f, 0.50f);
            MovementSpeed = System.Math.Max(2.0f, 4.0f + gearMoveSpeed);
            AttackSpeedBonus = System.Math.Clamp(gearAttackSpeed, 0f, 1.0f);
            BaseAttackSpeed = System.Math.Max(0.5f, 1.5f * (1.0f - AttackSpeedBonus));
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
            long gold = Gold;
            int slots = InventorySlots;

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
                        dbChar.Gold = gold;
                        dbChar.InventorySlots = slots;
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
