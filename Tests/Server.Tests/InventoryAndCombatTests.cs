using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Database;
using Server.Database.Models;
using Server.Handlers;
using Server.World;
using Server.World.Entities;
using Shared.Enums;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class InventoryAndCombatTests : IDisposable
    {
        private AppDbContext GetInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        public InventoryAndCombatTests()
        {
            DataManager.Initialize();

            // Ensure Map 1 exists for tests
            if (!GameLogic.MapMgr.ActiveMaps.ContainsKey(1))
            {
                GameLogic.MapMgr.ActiveMaps.TryAdd(1, new MapInstance(1));
            }
        }

        public void Dispose()
        {
            AppDbContext.Factory = () => new AppDbContext();
        }

        [Fact]
        public void ItemFactory_ShouldRollWithinTemplateRange()
        {
            // Template 1001 is Iron Broadsword: PhysicalAttack [12, 18], Strength [1, 3]
            var rng = new Random(42);
            var item = ItemFactory.CreateItem(1001, 1, 1, ItemRarity.Common, rng);

            Assert.NotNull(item);
            Assert.Equal(1001, item.TemplateId);
            Assert.InRange(item.RolledPhysicalAttack, 12, 18);
            Assert.InRange(item.RolledStrength, 1, 3);
            Assert.Equal(ItemRarity.Common, item.Rarity);
        }

        [Fact]
        public void ItemFactory_RarityMultipliers_ShouldBoostStatsAndAffixes()
        {
            // Template 1001 rolled as Rare (+25% base stats, 2 secondary affixes)
            var rng = new Random(100);
            var rareItem = ItemFactory.CreateItem(1001, 1, 1, ItemRarity.Rare, rng);

            Assert.NotNull(rareItem);
            Assert.Equal(ItemRarity.Rare, rareItem.Rarity);
            // Base max 18 * 1.25 = 23 (with rounding)
            Assert.True(rareItem.RolledPhysicalAttack >= 15);
        }

        [Fact]
        public void InventoryHandler_MoveAndSwap_ShouldUpdateSlots()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);

            var client = new MockClientConnection { AccountId = 1, PlayerId = 1001 };
            var player = new Player(1001, "InventoryTester", client);
            GameLogic.MapMgr.ActiveMaps[1].Players[1001] = player;

            var sword = ItemFactory.CreateItem(1001, player.Id, 1);
            sword.SlotIndex = 0;
            player.Inventory.Add(sword);

            // Move from Slot 0 to Slot 5
            using var movePacket = new Packet(OpCode.MoveInventoryItemRequest);
            movePacket.Write(0); // fromBag
            movePacket.Write(0); // fromSlot
            movePacket.Write(0); // toBag
            movePacket.Write(5); // toSlot
            using var readMovePacket = new Packet(movePacket.ToArray());

            InventoryHandler.HandleMoveItem(client, readMovePacket);

            Assert.Equal(5, sword.SlotIndex);

            // Add another item at Slot 2 and swap 5 and 2
            var tunic = ItemFactory.CreateItem(1301, player.Id, 1);
            tunic.SlotIndex = 2;
            player.Inventory.Add(tunic);

            using var swapPacket = new Packet(OpCode.MoveInventoryItemRequest);
            swapPacket.Write(0);
            swapPacket.Write(5);
            swapPacket.Write(0);
            swapPacket.Write(2);
            using var readSwapPacket = new Packet(swapPacket.ToArray());

            InventoryHandler.HandleMoveItem(client, readSwapPacket);

            Assert.Equal(2, sword.SlotIndex);
            Assert.Equal(5, tunic.SlotIndex);
        }

        [Fact]
        public void InventoryHandler_StackAndSplit_ShouldMaintainQuantities()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);

            var client = new MockClientConnection { AccountId = 1, PlayerId = 1002 };
            var player = new Player(1002, "StackTester", client);
            GameLogic.MapMgr.ActiveMaps[1].Players[1002] = player;

            // Template 3001 is Iron Ore (MaxStack 99)
            var ores = ItemFactory.CreateItem(3001, player.Id, 10);
            ores.SlotIndex = 0;
            player.Inventory.Add(ores);

            // Split 4 ores to slot 1
            using var splitPacket = new Packet(OpCode.SplitItemStackRequest);
            splitPacket.Write(0); // fromBag
            splitPacket.Write(0); // fromSlot
            splitPacket.Write(4); // split amount
            splitPacket.Write(0); // targetBag
            splitPacket.Write(1); // targetSlot
            using var readSplitPacket = new Packet(splitPacket.ToArray());

            InventoryHandler.HandleSplitStack(client, readSplitPacket);

            Assert.Equal(6, ores.Quantity);
            var splitOres = player.Inventory.FirstOrDefault(i => i.SlotIndex == 1);
            Assert.NotNull(splitOres);
            Assert.Equal(4, splitOres.Quantity);

            // Move slot 1 back to slot 0 (Stacking)
            using var stackPacket = new Packet(OpCode.MoveInventoryItemRequest);
            stackPacket.Write(0);
            stackPacket.Write(1);
            stackPacket.Write(0);
            stackPacket.Write(0);
            using var readStackPacket = new Packet(stackPacket.ToArray());

            InventoryHandler.HandleMoveItem(client, readStackPacket);

            Assert.Equal(10, ores.Quantity);
            Assert.Null(player.Inventory.FirstOrDefault(i => i.SlotIndex == 1));
        }

        [Fact]
        public void InventoryHandler_UseConsumable_ShouldRestoreHealth()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);

            var client = new MockClientConnection { AccountId = 1, PlayerId = 1003 };
            var player = new Player(1003, "PotionTester", client)
            {
                MaxHealth = 100,
                Health = 30
            };
            GameLogic.MapMgr.ActiveMaps[1].Players[1003] = player;

            // Template 2001 is Minor Health Potion (Restores 50 HP)
            var potion = ItemFactory.CreateItem(2001, player.Id, 2);
            potion.SlotIndex = 0;
            player.Inventory.Add(potion);

            using var usePacket = new Packet(OpCode.UseConsumableItemRequest);
            usePacket.Write(0);
            usePacket.Write(0);
            using var readUsePacket = new Packet(usePacket.ToArray());

            InventoryHandler.HandleUseConsumable(client, readUsePacket);

            Assert.Equal(80, player.Health);
            Assert.Equal(1, potion.Quantity);
        }

        [Fact]
        public void EquipmentHandler_EquipAndUnequip_ShouldUpdateStatsAndSlots()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);

            var client = new MockClientConnection { AccountId = 1, PlayerId = 1004 };
            var player = new Player(1004, "GearTester", client)
            {
                Level = 5,
                Strength = 10,
                Constitution = 10
            };
            GameLogic.MapMgr.ActiveMaps[1].Players[1004] = player;

            player.CalculateDerivedStats();
            int baseAttack = player.Attack;

            // Template 1001 is Iron Broadsword (MainHand, Rolled Attack ~15)
            var sword = ItemFactory.CreateItem(1001, player.Id, 1);
            sword.SlotIndex = 0;
            sword.RolledPhysicalAttack = 15;
            player.Inventory.Add(sword);

            // Equip sword to MainHand
            using var equipPacket = new Packet(OpCode.EquipItemRequest);
            equipPacket.Write(0); // bag
            equipPacket.Write(0); // slot
            equipPacket.Write((byte)EquipmentSlot.MainHand);
            using var readEquipPacket = new Packet(equipPacket.ToArray());

            EquipmentHandler.HandleEquipItem(client, readEquipPacket);

            Assert.True(sword.IsEquipped);
            Assert.Equal(EquipmentSlot.MainHand, sword.EquippedSlot);
            Assert.Empty(player.Inventory);
            Assert.True(player.Attack > baseAttack); // Attack multiplied by Strength!

            // Unequip sword
            using var unequipPacket = new Packet(OpCode.UnequipItemRequest);
            unequipPacket.Write((byte)EquipmentSlot.MainHand);
            using var readUnequipPacket = new Packet(unequipPacket.ToArray());

            EquipmentHandler.HandleUnequipItem(client, readUnequipPacket);

            Assert.False(sword.IsEquipped);
            Assert.Null(sword.EquippedSlot);
            Assert.Single(player.Inventory);
            Assert.Equal(baseAttack, player.Attack); // Returned to base attack
        }

        [Fact]
        public void EquipmentHandler_TwoHandedWeapon_ShouldUnequipOffHand()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);

            var client = new MockClientConnection { AccountId = 1, PlayerId = 1005 };
            var player = new Player(1005, "StaffTester", client) { Level = 5 };
            GameLogic.MapMgr.ActiveMaps[1].Players[1005] = player;

            // Equip a shield in OffHand
            var shield = ItemFactory.CreateItem(1101, player.Id, 1);
            shield.IsEquipped = true;
            shield.EquippedSlot = EquipmentSlot.OffHand;
            player.EquippedItems[EquipmentSlot.OffHand] = shield;

            // Put 2-Handed Wooden Staff (Template 1002) in inventory
            var staff = ItemFactory.CreateItem(1002, player.Id, 1);
            staff.SlotIndex = 0;
            player.Inventory.Add(staff);

            // Equip 2H Staff
            using var equipPacket = new Packet(OpCode.EquipItemRequest);
            equipPacket.Write(0);
            equipPacket.Write(0);
            equipPacket.Write((byte)EquipmentSlot.MainHand);
            using var readEquipPacket = new Packet(equipPacket.ToArray());

            EquipmentHandler.HandleEquipItem(client, readEquipPacket);

            Assert.True(staff.IsEquipped);
            Assert.False(player.EquippedItems.ContainsKey(EquipmentSlot.OffHand));
            Assert.False(shield.IsEquipped); // Shield was unequipped to bag!
            Assert.Contains(shield, player.Inventory);
        }

        [Fact]
        public void RefinementHandler_SafeUpgrade_ShouldSucceedAndBoostStats()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);

            var client = new MockClientConnection { AccountId = 1, PlayerId = 1006 };
            var player = new Player(1006, "UpgradeTester", client)
            {
                Gold = 500
            };
            GameLogic.MapMgr.ActiveMaps[1].Players[1006] = player;

            // Sword in bag Slot 0
            var sword = ItemFactory.CreateItem(1001, player.Id, 1);
            sword.SlotIndex = 0;
            sword.UpgradeLevel = 0;
            player.Inventory.Add(sword);

            // Catalyst: Weapon Enhancement Stone in Slot 1
            var stone = ItemFactory.CreateItem(3004, player.Id, 2);
            stone.SlotIndex = 1;
            player.Inventory.Add(stone);

            // Request Upgrade
            using var upPacket = new Packet(OpCode.UpgradeItemRequest);
            upPacket.Write(false); // isEquipped
            upPacket.Write(0);     // bag
            upPacket.Write(0);     // slot
            using var readUpPacket = new Packet(upPacket.ToArray());

            RefinementHandler.HandleUpgradeItem(client, readUpPacket);

            Assert.Equal(1, sword.UpgradeLevel); // +1 reached
            Assert.Equal(450, player.Gold);      // 50 gold deducted
            Assert.Equal(1, stone.Quantity);     // 1 catalyst deducted
        }

        [Fact]
        public void CraftingHandler_LearnAndCraft_ShouldConsumeMaterialsAndCreateItem()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);

            var client = new MockClientConnection { AccountId = 1, PlayerId = 1007 };
            var player = new Player(1007, "Crafter", client)
            {
                Gold = 300
            };
            GameLogic.MapMgr.ActiveMaps[1].Players[1007] = player;

            // Put Recipe: Steel Longsword (4001) in Slot 0
            var recipeItem = ItemFactory.CreateItem(4001, player.Id, 1);
            recipeItem.SlotIndex = 0;
            player.Inventory.Add(recipeItem);

            // Learn recipe
            using var learnPacket = new Packet(OpCode.LearnRecipeRequest);
            learnPacket.Write(0);
            learnPacket.Write(0);
            using var readLearnPacket = new Packet(learnPacket.ToArray());

            CraftingHandler.HandleLearnRecipe(client, readLearnPacket);

            Assert.Contains(4001, player.LearnedRecipes);
            Assert.Empty(player.Inventory); // Recipe item consumed

            // Add Ingredients: 5 Iron Ingots (3002) in Slot 0, 2 Oak Wood (3003) in Slot 1
            var ingots = ItemFactory.CreateItem(3002, player.Id, 5);
            ingots.SlotIndex = 0;
            player.Inventory.Add(ingots);

            var wood = ItemFactory.CreateItem(3003, player.Id, 2);
            wood.SlotIndex = 1;
            player.Inventory.Add(wood);

            // Craft recipe 4001
            using var craftPacket = new Packet(OpCode.CraftItemRequest);
            craftPacket.Write(4001);
            using var readCraftPacket = new Packet(craftPacket.ToArray());

            CraftingHandler.HandleCraftItem(client, readCraftPacket);

            Assert.Equal(250, player.Gold); // 50 gold deducted
            var craftedSword = player.Inventory.FirstOrDefault(i => i.TemplateId == 1004);
            Assert.NotNull(craftedSword);   // Steel Longsword crafted!
        }

        [Fact]
        public void CombatMath_3Pillars_ShouldCalculateCorrectly()
        {
            var player = new Player(1, "Warrior", null!)
            {
                Level = 10,
                Strength = 30,      // +30% weapon attack multiplier
                Constitution = 20,  // +30 defense
                Knowledge = 10      // +15 magic defense
            };

            // Equip sword with 40 Attack (+0)
            var sword = new CharacterItem
            {
                TemplateId = 1004,
                IsEquipped = true,
                EquippedSlot = EquipmentSlot.MainHand,
                RolledPhysicalAttack = 40,
                UpgradeLevel = 0
            };
            player.EquippedItems[EquipmentSlot.MainHand] = sword;

            player.CalculateDerivedStats();

            // Formula: 10 + (Level * 1.5) + (totalStr * 2) + (WeaponAttack * (1 + STR/100))
            // = 10 + 15 + 60 + (40 * 1.30) = 85 + 52 = 137
            Assert.Equal(137, player.Attack);

            // Defense mitigation test:
            // Attacker level 10, target Defense 80
            // Mitigation % = 80 / (80 + 40 * 10) = 80 / 480 = 16.66% reduction
            float reduction = (float)80 / (80 + (40f * 10));
            int mitigated = (int)Math.Round(player.Attack * (1.0f - reduction));

            Assert.True(mitigated > 0);
            Assert.True(mitigated < player.Attack);
            Assert.Equal(114, mitigated);
        }
    }
}
