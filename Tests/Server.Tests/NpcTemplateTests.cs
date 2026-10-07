using System;
using System.Collections.Generic;
using System.Linq;
using Server.Data;
using Server.Data.Models;
using Server.World;
using Server.World.Entities;
using Shared.Enums;
using Xunit;

namespace Server.Tests
{
    public class NpcTemplateTests
    {
        private static NpcTemplate Template(Action<NpcTemplate>? tweak = null)
        {
            var template = new NpcTemplate
            {
                TemplateId = 900,
                Name = "Test Mob",
                LevelRange = new[] { 3, 7 },
                StatVarianceRange = new[] { 0.8f, 1.2f },
                BaseStrength = 10,
                BaseConstitution = 10,
                BaseExp = 20
            };
            tweak?.Invoke(template);
            return template;
        }

        [Fact]
        public void ApplyTemplate_RollsLevelInsideRangeAndScalesExp()
        {
            var template = Template();
            var rng = new Random(1);
            var levels = new HashSet<int>();

            for (int i = 0; i < 200; i++)
            {
                var npc = new NPC(i + 1, template.Name);
                npc.ApplyTemplate(template, rng);

                Assert.InRange(npc.Level, 3, 7);
                Assert.Equal(20L * npc.Level, npc.ExpYield);
                levels.Add(npc.Level);
            }

            Assert.Equal(5, levels.Count); // every level of the range shows up, not just the minimum
        }

        [Fact]
        public void ApplyTemplate_VariesStatsInsideVarianceRange()
        {
            var template = Template();
            var rng = new Random(2);
            var strengths = new HashSet<int>();

            for (int i = 0; i < 200; i++)
            {
                var npc = new NPC(i + 1, template.Name);
                npc.ApplyTemplate(template, rng);

                Assert.InRange(npc.Strength, 8, 12);
                Assert.InRange(npc.Constitution, 8, 12);
                strengths.Add(npc.Strength);
            }

            Assert.True(strengths.Count > 1);
        }

        [Fact]
        public void ApplyTemplate_FixedRangesGiveIdenticalNpcs()
        {
            var template = Template(t => { t.LevelRange = new[] { 5, 5 }; t.StatVarianceRange = new[] { 1f, 1f }; });
            var a = new NPC(1, "a");
            var b = new NPC(2, "b");
            a.ApplyTemplate(template, new Random(3));
            b.ApplyTemplate(template, new Random(4));

            Assert.Equal(5, a.Level);
            Assert.Equal(a.MaxHealth, b.MaxHealth);
            Assert.Equal(10, a.Strength);
        }

        [Fact]
        public void ApplyTemplate_HigherLevelHasMoreHealthAndStartsFull()
        {
            var low = new NPC(1, "low");
            var high = new NPC(2, "high");
            low.ApplyTemplate(Template(t => t.LevelRange = new[] { 1, 1 }), new Random(5));
            high.ApplyTemplate(Template(t => t.LevelRange = new[] { 20, 20 }), new Random(5));

            Assert.True(high.MaxHealth > low.MaxHealth);
            Assert.Equal(high.MaxHealth, high.Health);
            Assert.Equal(high.MaxMana, high.Mana);
        }

        [Fact]
        public void FriendlyType_IsFriendlyBehaviorAndNpcEntityType_EvenWhenBehaviorSaysPassive()
        {
            var template = Template(t => { t.Type = "Friendly"; t.Behavior = "Passive"; });
            var npc = new NPC(1, template.Name);
            npc.ApplyTemplate(template, new Random(6));

            Assert.Equal(MobBehaviorType.Friendly, npc.BehaviorType);
            Assert.Equal(EntityType.Npc, npc.Type);
        }

        [Fact]
        public void EnemyType_StaysEnemy()
        {
            var template = Template(t => { t.Type = "Enemy"; t.Behavior = "Aggressive"; });
            var npc = new NPC(1, template.Name);
            npc.ApplyTemplate(template, new Random(7));

            Assert.Equal(MobBehaviorType.Aggressive, npc.BehaviorType);
            Assert.Equal(EntityType.Enemy, npc.Type);
        }

        [Fact]
        public void SpawnSpawners_AppliesRolledLevels()
        {
            var manager = new MapManager();
            manager.ActiveMaps[1] = new MapInstance(1);
            var templates = new Dictionary<int, NpcTemplate> { [900] = Template() };

            manager.SpawnSpawners(new[] { new SpawnerTemplate { TemplateId = 900, MapId = 1, Amount = 30 } }, templates);

            var npcs = manager.ActiveMaps[1].NPCs.Values.ToList();
            Assert.Equal(30, npcs.Count);
            Assert.All(npcs, n => Assert.InRange(n.Level, 3, 7));
            Assert.True(npcs.Select(n => n.Level).Distinct().Count() > 1);
        }

        [Fact]
        public void ShippedFriendlyNpcs_AreNotEnemies()
        {
            DataManager.Initialize();
            var friendly = DataManager.Npcs.Values.Where(n => n.IsFriendly).ToList();

            Assert.NotEmpty(friendly);
            Assert.All(friendly, n => Assert.Equal(MobBehaviorType.Friendly, n.BehaviorType));
        }
    }
}
