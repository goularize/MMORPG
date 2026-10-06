using System;
using System.Threading;
using Server.World.Entities;
using Xunit;

namespace Server.Tests
{
    // A concrete implementation of Entity just for testing
    public class TestEntity : Entity
    {
        public override Shared.Enums.EntityType Type => Shared.Enums.EntityType.Player;

        public TestEntity()
        {
            Id = 1;
            Name = "Test Character";
        }
        
        // Expose a way to artificially manipulate the last regen time for testing
        public void SetLastRegenTime(DateTime time)
        {
            _lastRegenTime = time;
        }

        // Allow reading the vitals changed flag
        public bool VitalsChanged => _vitalsChanged;
    }

    public class EntityStatsTests
    {
        public EntityStatsTests()
        {
            // Set base environment variables manually for tests
            Environment.SetEnvironmentVariable("BASE_HEALTH_RES", "5");
            Environment.SetEnvironmentVariable("BASE_MANA_RES", "3");
            Environment.SetEnvironmentVariable("REGEN_TICK_INTERVAL_SECONDS", "1");
            ServerConfig.Initialize();
        }

        [Fact]
        public void CalculateDerivedStats_Level1Strength10_ReturnsCorrectAttack()
        {
            // Arrange
            var entity = new TestEntity
            {
                Level = 1,
                Strength = 10
            };

            // Act
            entity.CalculateDerivedStats();

            // Assert
            // Attack = 10 + (10 * 2) + (1 * 1.5) = 10 + 20 + 1.5 = 31.5 -> cast to int -> 31
            Assert.Equal(31, entity.Attack);
        }

        [Fact]
        public void CalculateDerivedStats_Level10Constitution50_ReturnsCorrectMaxHealth()
        {
            // Arrange
            var entity = new TestEntity
            {
                Level = 10,
                Constitution = 50
            };

            // Act
            entity.CalculateDerivedStats();

            // Assert
            // MaxHealth = 50 + (50 * 10) + (10 * 15) = 50 + 500 + 150 = 700
            Assert.Equal(700, entity.MaxHealth);
        }

        [Fact]
        public void CalculateDerivedStats_NegativeAttributes_ClampsToMinimums()
        {
            // Arrange
            var entity = new TestEntity
            {
                Level = -5, // Edge case: someone manipulated their level
                Constitution = -100,
                Strength = -100
            };

            // Act
            entity.CalculateDerivedStats();

            // Assert
            Assert.True(entity.MaxHealth >= 1, "MaxHealth should not fall below 1");
            Assert.True(entity.Attack >= 1, "Attack should not fall below 1");
        }

        [Fact]
        public void Update_AfterRegenInterval_IncreasesHealthByResAmount()
        {
            // Arrange
            var entity = new TestEntity
            {
                MaxHealth = 100,
                Health = 50,
                MaxMana = 100,
                Mana = 50
            };
            
            // Push the last regen time into the past by exactly the interval plus a margin
            entity.SetLastRegenTime(DateTime.UtcNow.AddSeconds(-1.1));

            // Act
            entity.Update();

            // Assert
            Assert.Equal(55, entity.Health);
            Assert.Equal(53, entity.Mana);
            Assert.True(entity.VitalsChanged);
        }

        [Fact]
        public void Update_NearMaxHealth_CapsAtMaxHealth()
        {
            // Arrange
            var entity = new TestEntity
            {
                MaxHealth = 100,
                Health = 98
            };
            
            entity.SetLastRegenTime(DateTime.UtcNow.AddSeconds(-1.1));

            // Act
            entity.Update();

            // Assert
            // HP should increase by 5 (base) but cap at 100, not 103.
            Assert.Equal(100, entity.Health);
        }

        [Fact]
        public void Update_SpammingCalls_DoesNotDoubleRegen()
        {
            // Arrange
            var entity = new TestEntity
            {
                MaxHealth = 100,
                Health = 50
            };
            
            // Force one valid regen
            entity.SetLastRegenTime(DateTime.UtcNow.AddSeconds(-1.1));

            // Act
            entity.Update();
            int healthAfterFirstRegen = entity.Health; // Should be 55
            
            // Spam update without time passing
            entity.Update();
            entity.Update();

            // Assert
            Assert.Equal(55, healthAfterFirstRegen);
            Assert.Equal(55, entity.Health); // Should not have increased further
        }

        [Fact]
        public void Update_WhenDead_DoesNotRegen()
        {
            // Arrange
            var entity = new TestEntity
            {
                MaxHealth = 100,
                Health = 0 // Dead
            };
            
            // Push time way past interval
            entity.SetLastRegenTime(DateTime.UtcNow.AddSeconds(-10));

            // Act
            entity.Update();

            // Assert
            Assert.Equal(0, entity.Health); // Should still be dead
        }

        [Fact]
        public void Update_RightAfterCombat_DoesNotRegen()
        {
            var entity = new TestEntity { MaxHealth = 100, Health = 50, MaxMana = 100, Mana = 50 };
            entity.SetLastRegenTime(DateTime.UtcNow.AddSeconds(-10));
            entity.MarkInCombat();

            entity.Update();

            Assert.Equal(50, entity.Health);
            Assert.Equal(50, entity.Mana);
            Assert.False(entity.VitalsChanged);
        }

        [Fact]
        public void Update_BeforeCooldownElapsed_DoesNotRegen()
        {
            var entity = new TestEntity { MaxHealth = 100, Health = 50 };
            entity.SetLastRegenTime(DateTime.UtcNow.AddSeconds(-10));
            entity.LastCombatTime = DateTime.UtcNow.AddSeconds(-(ServerConfig.RegenCooldownSeconds - 2));

            entity.Update();

            Assert.Equal(50, entity.Health);
        }

        [Fact]
        public void Update_AfterCooldownElapsed_ResumesRegen()
        {
            var entity = new TestEntity { MaxHealth = 100, Health = 50 };
            entity.SetLastRegenTime(DateTime.UtcNow.AddSeconds(-10));
            entity.LastCombatTime = DateTime.UtcNow.AddSeconds(-(ServerConfig.RegenCooldownSeconds + 1));

            entity.Update();

            Assert.Equal(55, entity.Health);
        }

        [Fact]
        public void Update_UninvolvedEntity_IsNotBlockedByAnotherEntitysCombat()
        {
            var fighter = new TestEntity { MaxHealth = 100, Health = 50 };
            var bystander = new TestEntity { MaxHealth = 100, Health = 50 };
            fighter.SetLastRegenTime(DateTime.UtcNow.AddSeconds(-10));
            bystander.SetLastRegenTime(DateTime.UtcNow.AddSeconds(-10));
            fighter.MarkInCombat();

            fighter.Update();
            bystander.Update();

            Assert.Equal(50, fighter.Health);
            Assert.Equal(55, bystander.Health);
        }

        [Fact]
        public void Initialize_RegenCooldown_ParsedInvariantAndDefaultsWhenMissing()
        {
            try
            {
                Environment.SetEnvironmentVariable("REGEN_COOLDOWN_SECONDS", "2.5");
                ServerConfig.Initialize();
                Assert.Equal(2.5, ServerConfig.RegenCooldownSeconds);

                Environment.SetEnvironmentVariable("REGEN_COOLDOWN_SECONDS", null);
                ServerConfig.Initialize();
                Assert.Equal(5.0, ServerConfig.RegenCooldownSeconds);
            }
            finally
            {
                Environment.SetEnvironmentVariable("REGEN_COOLDOWN_SECONDS", null);
                ServerConfig.Initialize();
            }
        }
    }
}
