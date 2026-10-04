using System;
using System.Collections.Generic;
using Server.Data;
using Server.Database.Models;
using Shared.Enums;
using Shared.Math;
using Shared.Network;

namespace Server.World.Entities
{
    public class NPC : Entity
    {
        public override Shared.Enums.EntityType Type => Shared.Enums.EntityType.Enemy;
        public override string PrefabName => Server.Data.DataManager.Npcs.TryGetValue(TemplateId, out var template) ? template.PrefabName : "Enemy_Slime";

        public int SpawnId { get; set; }
        public int TemplateId { get; set; }
        public MobBehaviorType BehaviorType { get; set; } = MobBehaviorType.Passive;
        public AIState CurrentState { get; set; } = AIState.Idle;

        public Vector3 SpawnPosition { get; set; }
        public Vector3 WanderDestination { get; set; }
        public Player? Target { get; set; }

        public float AggroRadius { get; set; } = 8.0f;
        public float WanderRadius { get; set; } = 5.0f;
        public float LeashRadius { get; set; } = 20.0f;
        public float AttackRange { get; set; } = 2.0f;
        public float WalkSpeed { get; set; } = 2.0f;
        public float RunSpeed { get; set; } = 3.5f;
        public float RespawnTimeSeconds { get; set; } = 15.0f;
        public float PackAssistRadius { get; set; } = 12.0f;
        public long ExpYield { get; set; } = 25;

        public float CorpseDecaySeconds { get; set; } = 3.0f;
        private DateTime _deathTime = DateTime.MinValue;
        public bool IsCorpseDecayed => (DateTime.UtcNow - _deathTime).TotalSeconds >= CorpseDecaySeconds;

        private DateTime _stateTimer = DateTime.UtcNow;
        private float _idleDuration = 3.0f;

        public MapInstance? Map { get; set; }

        private static readonly Random _rng = new Random();

        public NPC(int id, string name)
        {
            Id = id;
            Name = name;
            BaseAttackSpeed = 1.5f;
            CalculateDerivedStats();
        }

        public override void Update()
        {
            if (Map == null)
            {
                Map = GameLogic.MapMgr.GetMap(MapId);
            }

            UpdateAI(Map, 1.0f / 30.0f);
        }

        public void UpdateAI(MapInstance? map, float dt)
        {
            if (CurrentState == AIState.Dead)
            {
                HandleDead(map, dt);
                return;
            }

            // Passive vitals regeneration when not dead
            base.Update();

            // Validate active target if chasing or attacking
            if (Target != null)
            {
                if (Target.Health <= 0 || Target.MapId != MapId || (map != null && !map.Players.ContainsKey(Target.Id)))
                {
                    // Target died, logged out, or changed maps
                    Target = null;
                    CurrentState = AIState.ReturnToSpawn;
                }
                else if (Vector3.Distance(SpawnPosition, Target.Position) > LeashRadius ||
                         Vector3.Distance(Position, SpawnPosition) > LeashRadius)
                {
                    // Target evaded beyond leash radius
                    Console.WriteLine($"[Mob AI] {Name} ({Id}) lost target {Target.Name} (leashed).");
                    Target = null;
                    CurrentState = AIState.ReturnToSpawn;
                }
            }

            switch (CurrentState)
            {
                case AIState.Idle:
                    HandleIdle(map, dt);
                    break;
                case AIState.Wander:
                    HandleWander(map, dt);
                    break;
                case AIState.Chase:
                    HandleChase(map, dt);
                    break;
                case AIState.Attack:
                    HandleAttack(map, dt);
                    break;
                case AIState.ReturnToSpawn:
                    HandleReturnToSpawn(map, dt);
                    break;
            }
        }

        private void HandleIdle(MapInstance? map, float dt)
        {
            if (BehaviorType == MobBehaviorType.Aggressive)
            {
                if (CheckAggro(map)) return;
            }

            if ((DateTime.UtcNow - _stateTimer).TotalSeconds >= _idleDuration)
            {
                if (WanderRadius > 0.1f)
                {
                    float angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
                    float distance = (float)(_rng.NextDouble() * WanderRadius);
                    Vector3 candidate = new Vector3(
                        SpawnPosition.X + (float)Math.Cos(angle) * distance,
                        SpawnPosition.Y + (float)Math.Sin(angle) * distance,
                        SpawnPosition.Z
                    );

                    if (map == null || map.IsWalkable(candidate))
                    {
                        WanderDestination = candidate;
                        CurrentState = AIState.Wander;
                        _stateTimer = DateTime.UtcNow;
                        return;
                    }
                }

                _stateTimer = DateTime.UtcNow;
                _idleDuration = 2.0f + (float)(_rng.NextDouble() * 3.0);
            }
        }

        private void HandleWander(MapInstance? map, float dt)
        {
            if (BehaviorType == MobBehaviorType.Aggressive)
            {
                if (CheckAggro(map)) return;
            }

            // Timeout wandering if obstructed or taking too long
            if ((DateTime.UtcNow - _stateTimer).TotalSeconds > 8.0)
            {
                CurrentState = AIState.Idle;
                _stateTimer = DateTime.UtcNow;
                _idleDuration = 2.0f + (float)(_rng.NextDouble() * 3.0);
                return;
            }

            float dist = Vector3.Distance(Position, WanderDestination);
            if (dist <= 0.2f)
            {
                Position = WanderDestination;
                CurrentState = AIState.Idle;
                _stateTimer = DateTime.UtcNow;
                _idleDuration = 2.0f + (float)(_rng.NextDouble() * 3.0);
            }
            else
            {
                Vector3 nextPos = Vector3.MoveTowards(Position, WanderDestination, WalkSpeed * dt);
                if (map == null || map.IsWalkable(nextPos))
                {
                    Position = nextPos;
                }
                else
                {
                    CurrentState = AIState.Idle;
                    _stateTimer = DateTime.UtcNow;
                    _idleDuration = 2.0f + (float)(_rng.NextDouble() * 3.0);
                }
            }
        }

        private void HandleChase(MapInstance? map, float dt)
        {
            if (Target == null)
            {
                CurrentState = AIState.ReturnToSpawn;
                return;
            }

            float dist = Vector3.Distance(Position, Target.Position);
            if (dist <= AttackRange)
            {
                CurrentState = AIState.Attack;
                if ((DateTime.UtcNow - LastAttackTime).TotalSeconds >= BaseAttackSpeed)
                {
                    LastAttackTime = DateTime.UtcNow;
                    PerformAttack(map, Target);
                }
                return;
            }

            Vector3 nextPos = Vector3.MoveTowards(Position, Target.Position, RunSpeed * dt);
            if (map == null || map.IsWalkable(nextPos))
            {
                Position = nextPos;
            }
            else
            {
                // Simple wall sliding
                Vector3 slideX = new Vector3(nextPos.X, Position.Y, Position.Z);
                Vector3 slideY = new Vector3(Position.X, nextPos.Y, Position.Z);
                if (map.IsWalkable(slideX))
                {
                    Position = slideX;
                }
                else if (map.IsWalkable(slideY))
                {
                    Position = slideY;
                }
            }
        }

        private void HandleAttack(MapInstance? map, float dt)
        {
            if (Target == null)
            {
                CurrentState = AIState.ReturnToSpawn;
                return;
            }

            float dist = Vector3.Distance(Position, Target.Position);
            if (dist > AttackRange)
            {
                CurrentState = AIState.Chase;
                return;
            }

            if ((DateTime.UtcNow - LastAttackTime).TotalSeconds >= BaseAttackSpeed)
            {
                LastAttackTime = DateTime.UtcNow;
                PerformAttack(map, Target);
            }
        }

        public void PerformAttack(MapInstance? map, Player target)
        {
            bool isDodge = _rng.NextDouble() < target.DodgeChance;
            int finalDamage = 0;
            bool isCrit = false;

            if (!isDodge)
            {
                float reduction = (float)target.Defense / (target.Defense + (40f * Math.Max(1, Level)));
                int baseDamage = Math.Max(1, (int)Math.Round(Attack * (1.0f - reduction)));
                double variance = 0.9 + (_rng.NextDouble() * 0.2);
                baseDamage = (int)(baseDamage * variance);

                if (_rng.NextDouble() < CritChance)
                {
                    isCrit = true;
                    baseDamage = (int)(baseDamage * CritMultiplier);
                }

                finalDamage = Math.Max(1, baseDamage);
                target.Health -= finalDamage;
                if (target.Health < 0) target.Health = 0;
            }

            Console.WriteLine($"[Mob Combat] {Name} hit {target.Name} for {finalDamage} damage! (Crit: {isCrit}, Dodge: {isDodge}) ({target.Health}/{target.MaxHealth})");

            // Broadcast combat event packet
            if (map != null)
            {
                using Packet combatPacket = new Packet(OpCode.EntityCombatEvent);
                combatPacket.Write(Id);
                combatPacket.Write(target.Id);
                combatPacket.Write(finalDamage);
                combatPacket.Write(isCrit);
                combatPacket.Write(isDodge);
                map.Broadcast(combatPacket, Position);
            }

            // Sync vitals to target player
            using Packet vitalsPacket = new Packet(OpCode.VitalsUpdate);
            vitalsPacket.Write(target.Id);
            vitalsPacket.Write(target.Health);
            vitalsPacket.Write(target.Mana);
            target.Connection?.Send(vitalsPacket);

            // If target died from this attack
            if (target.Health <= 0)
            {
                Console.WriteLine($"[Mob AI] {Name} defeated player {target.Name}.");
                Target = null;
                CurrentState = AIState.ReturnToSpawn;
            }
        }

        private void HandleReturnToSpawn(MapInstance? map, float dt)
        {
            float dist = Vector3.Distance(Position, SpawnPosition);
            if (dist <= 0.2f)
            {
                Position = SpawnPosition;
                Health = MaxHealth; // Reset vitals when successfully leashed back
                CurrentState = AIState.Idle;
                _stateTimer = DateTime.UtcNow;
                _idleDuration = 3.0f;
            }
            else
            {
                Vector3 nextPos = Vector3.MoveTowards(Position, SpawnPosition, RunSpeed * dt);
                if (map == null || map.IsWalkable(nextPos))
                {
                    Position = nextPos;
                }
                else
                {
                    // If obstructed on path home, snap directly to spawn to prevent getting stuck
                    Position = SpawnPosition;
                    Health = MaxHealth;
                    CurrentState = AIState.Idle;
                    _stateTimer = DateTime.UtcNow;
                }
            }
        }

        private void HandleDead(MapInstance? map, float dt)
        {
            if ((DateTime.UtcNow - _deathTime).TotalSeconds >= RespawnTimeSeconds)
            {
                Respawn(map);
            }
        }

        public void Die(MapInstance? map, Player? killer = null)
        {
            CurrentState = AIState.Dead;
            Target = null;
            Health = 0;
            _deathTime = DateTime.UtcNow;
            Console.WriteLine($"[Mob Defeated] {Name} ({Id}) was killed.");

            if (map != null && killer != null)
            {
                DropLoot(map, killer);
            }
        }

        private void DropLoot(MapInstance map, Player killer)
        {
            if (!DataManager.LootTables.TryGetValue(TemplateId, out var lootTable))
            {
                return;
            }

            long droppedGold = 0;
            if (lootTable.MaxGold > 0 && _rng.NextDouble() <= lootTable.GoldChance)
            {
                droppedGold = _rng.Next(lootTable.MinGold, lootTable.MaxGold + 1);
            }

            var droppedItems = new List<CharacterItem>();
            foreach (var entry in lootTable.Entries)
            {
                if (_rng.NextDouble() <= entry.DropChance)
                {
                    int qty = _rng.Next(entry.MinQuantity, entry.MaxQuantity + 1);
                    var item = ItemFactory.CreateItem(entry.ItemTemplateId, 0, qty, null, _rng);
                    if (item != null)
                    {
                        droppedItems.Add(item);
                    }
                }
            }

            if (droppedGold > 0 || droppedItems.Count > 0)
            {
                var satchel = new LootSatchel(Position, killer.Id, droppedGold, droppedItems);
                map.SpawnLootSatchel(satchel);
            }
        }

        public void Respawn(MapInstance? map)
        {
            Position = SpawnPosition;
            Health = MaxHealth;
            Mana = MaxMana;
            CurrentState = AIState.Idle;
            _stateTimer = DateTime.UtcNow;
            _idleDuration = 3.0f;
            Console.WriteLine($"[Mob Respawn] {Name} ({Id}) respawned at {SpawnPosition}.");
        }

        public bool CheckAggro(MapInstance? map)
        {
            if (map == null || AggroRadius <= 0.1f) return false;

            Player? closest = null;
            float closestDist = float.MaxValue;

            foreach (var player in map.Players.Values)
            {
                if (player.Health <= 0) continue;

                float dist = Vector3.Distance(Position, player.Position);
                if (dist <= AggroRadius && dist < closestDist)
                {
                    closest = player;
                    closestDist = dist;
                }
            }

            if (closest != null)
            {
                Target = closest;
                CurrentState = AIState.Chase;
                Console.WriteLine($"[Mob Aggro] {Name} ({Id}) spotted {closest.Name} and started chasing!");
                return true;
            }

            return false;
        }

        public void OnAttacked(Entity attacker, MapInstance? map)
        {
            if (BehaviorType == MobBehaviorType.Friendly) return;
            if (CurrentState == AIState.Dead) return;

            if (attacker is Player player)
            {
                // Retaliate if not already fighting or dead
                if (CurrentState != AIState.Attack && CurrentState != AIState.Chase)
                {
                    Target = player;
                    CurrentState = AIState.Chase;
                }

                // If Pack behavior, alert nearby pack mates!
                if (BehaviorType == MobBehaviorType.Pack && map != null)
                {
                    AlertPack(map, player);
                }
            }
        }

        public void AlertPack(MapInstance map, Player attacker)
        {
            foreach (var other in map.NPCs.Values)
            {
                if (other.Id == Id) continue;
                if (other.BehaviorType != MobBehaviorType.Pack) continue;
                if (other.CurrentState == AIState.Dead || other.CurrentState == AIState.ReturnToSpawn) continue;

                float dist = Vector3.Distance(Position, other.Position);
                if (dist <= PackAssistRadius)
                {
                    if (other.Target == null || other.Target.Health <= 0)
                    {
                        other.Target = attacker;
                        other.CurrentState = AIState.Chase;
                        Console.WriteLine($"[Pack Alert] {Name} ({Id}) called for help! {other.Name} ({other.Id}) joined the fight against {attacker.Name}!");
                    }
                }
            }
        }
    }
}
