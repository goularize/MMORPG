using System;
using System.Linq;
using Server.Data;
using Server.Database.Models;
using Server.World;
using Server.World.Entities;
using Shared.Enums;
using Xunit;

namespace Server.Tests
{
    // Player with access to the protected regen timer
    internal class RegenTestPlayer : Player
    {
        public RegenTestPlayer(int id, string name, Server.Network.IClientConnection connection) : base(id, name, connection) { }
        public void SetLastRegenTime(DateTime time) => _lastRegenTime = time;
    }

    public class ItemAffixTests
    {
        public ItemAffixTests()
        {
            DataManager.Initialize();
            Environment.SetEnvironmentVariable("BASE_HEALTH_RES", "5");
            Environment.SetEnvironmentVariable("BASE_MANA_RES", "3");
            Environment.SetEnvironmentVariable("REGEN_TICK_INTERVAL_SECONDS", "1");
            Environment.SetEnvironmentVariable("REGEN_COOLDOWN_SECONDS", "5");
            ServerConfig.Initialize();
        }

        [Fact]
        public void TemplateCritChanceRange_IsRolled_EvenOnCommonItems()
        {
            // Iron Broadsword (1001) has CritChance 1-3 => 1%-3%
            var crits = Enumerable.Range(0, 300)
                .Select(seed => ItemFactory.CreateItem(1001, 0, 1, ItemRarity.Common, new Random(seed))!.RolledCritChance)
                .ToList();

            Assert.All(crits, c => Assert.InRange(c, 0.01f - 1e-6f, 0.03f + 1e-6f));
            Assert.True(crits.Distinct().Count() > 1, "the range should produce different rolls");
        }

        [Fact]
        public void ItemsWithoutATemplateCritRange_DoNotGetOneOnCommon()
        {
            // Wooden Staff (1002) has an empty 0-0 range and Common items have no random affixes
            var item = ItemFactory.CreateItem(1002, 0, 1, ItemRarity.Common, new Random(1))!;

            Assert.Equal(0f, item.RolledCritChance);
        }

        private static RegenTestPlayer PlayerWith(params CharacterItem[] equipped)
        {
            var player = new RegenTestPlayer(1, "Regen", new MockClientConnection { PlayerId = 1 }) { Level = 1 };
            int slot = 0;
            foreach (var item in equipped) player.EquippedItems[(EquipmentSlot)(++slot)] = item;
            player.CalculateDerivedStats();
            return player;
        }

        [Fact]
        public void EquippedRegenAffixes_AreSummedIntoTheRegenBonus()
        {
            var player = PlayerWith(
                new CharacterItem { RolledHealthRegen = 3, RolledManaRegen = 2 },
                new CharacterItem { RolledHealthRegen = 4 });

            Assert.Equal(7, player.HealthRegenBonus);
            Assert.Equal(2, player.ManaRegenBonus);
        }

        [Fact]
        public void RegenBonus_IsAddedToEachRegenTick_AndDropsWhenUnequipped()
        {
            var player = PlayerWith(new CharacterItem { RolledHealthRegen = 4, RolledManaRegen = 6 });
            player.Health = 10;
            player.Mana = 10;
            player.LastCombatTime = DateTime.UtcNow.AddMinutes(-1);
            player.SetLastRegenTime(DateTime.UtcNow.AddSeconds(-2));

            player.Update();

            Assert.Equal(10 + 5 + 4, player.Health); // BaseHealthRes 5 + gear 4
            Assert.Equal(10 + 3 + 6, player.Mana);   // BaseManaRes 3 + gear 6

            player.EquippedItems.Clear();
            player.CalculateDerivedStats();
            Assert.Equal(0, player.HealthRegenBonus);
            Assert.Equal(0, player.ManaRegenBonus);
        }
    }
}
