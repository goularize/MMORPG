using System;
using System.Collections.Generic;

namespace Server.World.Entities
{
    public class Player : Entity
    {
        public override Shared.Enums.EntityType Type => Shared.Enums.EntityType.Player;

        // AoI Tracking
        public HashSet<int> KnownEntities { get; } = new();

        // Set when the player leaves a map; the game loop clears KnownEntities so every nearby entity is
        // spawned again on the client after a respawn or re-entry (the set is only touched on the game thread).
        public volatile bool AoiResetRequested;

        // Account Relationship
        public int AccountId { get; set; }
        public Server.Network.IClientConnection Connection { get; set; } // The active network session

        // RPG Progression
        public long Exp { get; set; }
        public int StatPoints { get; set; }
        
        // Currency & Inventory
        public long Gold { get; set; } = 0;

        // Chat flood control (game thread only)
        public Server.Network.TokenBucket ChatBucket { get; } = new(ServerConfig.ChatBurst, ServerConfig.ChatMessagesPerSecond);
        public bool ChatLimitNotified { get; set; }
        public int InventorySlots { get; set; } = 20;
        public Dictionary<Shared.Enums.EquipmentSlot, Server.Database.Models.CharacterItem> EquippedItems { get; } = new();
        public List<Server.Database.Models.CharacterItem> Inventory { get; } = new();
        public HashSet<int> LearnedRecipes { get; } = new();

        // Movement tracking
        public System.DateTime LastMoveTime { get; set; } = System.DateTime.UtcNow;
        // Bind Location
        public int BindMapId { get; set; }
        public Shared.Math.Vector3 BindPosition { get; set; }

        /// <summary>
        /// True from world entry until the client reports WorldReadyRequest (its map scene is loaded). While set the
        /// character is invisible to others, receives no world traffic and cannot act. Game thread only.
        /// </summary>
        public bool AwaitingWorldReady { get; private set; }

        /// <summary>When an unready client is disconnected (see ServerConfig.WorldReadyTimeoutSeconds); null when not waiting or disabled.</summary>
        public System.DateTime? WorldReadyDeadlineUtc { get; private set; }

        public void BeginAwaitingWorldReady()
        {
            AwaitingWorldReady = true;
            WorldReadyDeadlineUtc = ServerConfig.WorldReadyTimeoutSeconds > 0
                ? DateTime.UtcNow.AddSeconds(ServerConfig.WorldReadyTimeoutSeconds)
                : null;
            AoiResetRequested = true; // the client knows nothing yet: everything around is spawned once it is ready
        }

        public void MarkWorldReady()
        {
            AwaitingWorldReady = false;
            WorldReadyDeadlineUtc = null;
            AoiResetRequested = true;
        }

        public bool IsWorldReadyOverdue(DateTime utcNow) => AwaitingWorldReady && WorldReadyDeadlineUtc.HasValue && utcNow >= WorldReadyDeadlineUtc.Value;

        /// <summary>
        /// Set while the character stays in the world after its connection dropped in combat (combat-log
        /// protection); the map removes it when this passes. Null for a normally connected player.
        /// </summary>
        public System.DateTime? LingerUntilUtc { get; set; }
        public bool IsLingering => LingerUntilUtc.HasValue;

        public Player(int id, string name, Server.Network.IClientConnection connection)
        {
            Id = id;
            Name = name;
            Connection = connection;
        }

        /// <summary>
        /// The connection dropped while the character was in combat: keep it in the world, unable to act, for
        /// ServerConfig.CombatLogoutLingerSeconds so pulling the plug is not an escape from the fight.
        /// </summary>
        public void BeginLinger()
        {
            Connection = Server.Network.DetachedConnection.Instance;
            LingerUntilUtc = DateTime.UtcNow.AddSeconds(ServerConfig.CombatLogoutLingerSeconds);

            // If the server dies while the character lingers, at least the state at disconnect is on disk
            QueueSave(urgent: true);
        }

        /// <summary>Full vitals at the character's bind point (respawn). The caller removes/re-adds it to the right map.</summary>
        public void ReviveAtBindPoint()
        {
            Health = MaxHealth;
            Mana = MaxMana;
            MapId = BindMapId;
            Position = BindPosition;
        }

        /// <summary>A lingering character is picked up by a new session instead of being loaded a second time.</summary>
        public void Reattach(Server.Network.IClientConnection connection)
        {
            Connection = connection;
            LingerUntilUtc = null;
            BeginAwaitingWorldReady(); // the new client knows nothing yet: it loads the scene, then everything is re-sent
        }

        /// <summary>True once a lingering character should leave the world (time is up, or it died).</summary>
        public bool IsLingerOver(DateTime utcNow) => LingerUntilUtc.HasValue && (utcNow >= LingerUntilUtc.Value || Health <= 0);

        // Periodic safety-net save, staggered per player so autosaves never arrive as one burst
        private DateTime _nextAutosaveUtc;

        public override void Update()
        {
            base.Update();

            var utcNow = DateTime.UtcNow;
            if (_nextAutosaveUtc == default)
            {
                _nextAutosaveUtc = utcNow.AddSeconds(ServerConfig.AutosaveSeconds * (0.1 + Random.Shared.NextDouble() * 0.9));
            }
            else if (utcNow >= _nextAutosaveUtc)
            {
                QueueSave();
                _nextAutosaveUtc = utcNow.AddSeconds(ServerConfig.AutosaveSeconds);
            }
            
            // Sync vitals to client if they changed (e.g. from regen)
            if (_vitalsChanged && !AwaitingWorldReady)
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

            // Level-ups are durable events
            QueueSave(urgent: true);
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
            int gearHealthRegen = 0;
            int gearManaRegen = 0;

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
                gearHealthRegen += item.RolledHealthRegen;
                gearManaRegen += item.RolledManaRegen;
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
            HealthRegenBonus = gearHealthRegen;
            ManaRegenBonus = gearManaRegen;
            MovementSpeed = System.Math.Max(Shared.Constants.GameRules.MinPlayerMoveSpeed, Shared.Constants.GameRules.BasePlayerMoveSpeed + gearMoveSpeed);
            AttackSpeedBonus = System.Math.Clamp(gearAttackSpeed, 0f, 1.0f);
            BaseAttackSpeed = System.Math.Max(Shared.Constants.GameRules.MinPlayerAttackInterval, Shared.Constants.GameRules.BasePlayerAttackInterval * (1.0f - AttackSpeedBonus));
        }

        /// <summary>
        /// Takes an immutable snapshot of everything persisted on the character row. Call on the game thread:
        /// the persistence workers only ever see this copy, never the live player.
        /// </summary>
        public Server.Persistence.CharacterState CreatePersistenceState() => new(
            Id, Level, Exp, StatPoints, Health, Mana, Strength, Intelligence, Constitution, Knowledge,
            Gold, InventorySlots, MapId, Position.X, Position.Y, Position.Z,
            BindMapId, BindPosition.X, BindPosition.Y, BindPosition.Z);

        /// <summary>Queues a background save of this character (write-behind, coalesced with earlier unflushed saves).</summary>
        /// <param name="urgent">Durable event (value created/destroyed/transferred, level-up, disconnect): flush promptly.</param>
        public void QueueSave(bool urgent = false)
            => Server.Persistence.PersistenceService.Instance.QueueCharacter(CreatePersistenceState(), urgent);
    }
}
