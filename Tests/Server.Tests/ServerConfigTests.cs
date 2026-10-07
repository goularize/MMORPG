using System;
using System.Globalization;
using Xunit;

namespace Server.Tests
{
    public class ServerConfigTests : IDisposable
    {
        private static readonly string[] Keys =
        {
            "SERVER_PORT", "DEFAULT_CHARACTER_SLOT", "REGEN_TICK_INTERVAL_SECONDS", "REGEN_COOLDOWN_SECONDS",
            "COMMAND_BUDGET_MS", "EXP_GROWTH_RATE", "LOOT_OWNERSHIP_SECONDS", "LOOT_DECAY_SECONDS",
            "BASE_HEALTH_RES", "MAX_PENDING_COMMANDS_PER_CLIENT", "COMBAT_TAG_SECONDS", "COMBAT_LOGOUT_LINGER_SECONDS"
        };

        private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;

        public ServerConfigTests() => Reset();

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            Reset();
        }

        private static void Reset()
        {
            foreach (var key in Keys) Environment.SetEnvironmentVariable(key, null);
            // The other test classes expect these (they are part of the suite's baseline)
            Environment.SetEnvironmentVariable("BASE_HEALTH_RES", "5");
            Environment.SetEnvironmentVariable("BASE_MANA_RES", "3");
            Environment.SetEnvironmentVariable("REGEN_TICK_INTERVAL_SECONDS", "1");
            ServerConfig.Initialize();
        }

        [Theory]
        [InlineData("pt-BR")]
        [InlineData("de-DE")]
        [InlineData("en-US")]
        public void DecimalSettings_AreParsedWithThePointSeparator_WhateverTheHostCulture(string culture)
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Environment.SetEnvironmentVariable("REGEN_TICK_INTERVAL_SECONDS", "0.5");
            Environment.SetEnvironmentVariable("REGEN_COOLDOWN_SECONDS", "2.5");
            Environment.SetEnvironmentVariable("LOOT_OWNERSHIP_SECONDS", "1.5");
            Environment.SetEnvironmentVariable("LOOT_DECAY_SECONDS", "0.25");
            Environment.SetEnvironmentVariable("COMMAND_BUDGET_MS", "7.5");
            Environment.SetEnvironmentVariable("EXP_GROWTH_RATE", "1.75");

            ServerConfig.Initialize();

            Assert.Equal(0.5, ServerConfig.RegenTickIntervalSeconds);
            Assert.Equal(2.5, ServerConfig.RegenCooldownSeconds);
            Assert.Equal(1.5f, ServerConfig.LootOwnershipSeconds);
            Assert.Equal(0.25f, ServerConfig.LootDecaySeconds);
            Assert.Equal(7.5, ServerConfig.CommandBudgetMs);
            Assert.Equal(1.75, ServerConfig.ExpGrowthRate);
        }

        [Fact]
        public void CombatLogSettings_DefaultTo10And15_AndCanBeOverriddenOrDisabled()
        {
            Assert.Equal(10.0, ServerConfig.CombatTagSeconds);
            Assert.Equal(15.0, ServerConfig.CombatLogoutLingerSeconds);

            Environment.SetEnvironmentVariable("COMBAT_TAG_SECONDS", "4.5");
            Environment.SetEnvironmentVariable("COMBAT_LOGOUT_LINGER_SECONDS", "0");
            ServerConfig.Initialize();

            Assert.Equal(4.5, ServerConfig.CombatTagSeconds);
            Assert.Equal(0.0, ServerConfig.CombatLogoutLingerSeconds);

            Environment.SetEnvironmentVariable("COMBAT_TAG_SECONDS", "-3"); // invalid: falls back
            ServerConfig.Initialize();
            Assert.Equal(10.0, ServerConfig.CombatTagSeconds);
        }

        [Fact]
        public void MissingValues_UseTheDefaults()
        {
            Assert.Equal(7777, ServerConfig.ServerPort);
            Assert.Equal(3, ServerConfig.DefaultCharacterSlots);
            Assert.Equal(5.0, ServerConfig.RegenCooldownSeconds);
            Assert.Equal(30f, ServerConfig.LootOwnershipSeconds);
            Assert.Equal(128, ServerConfig.MaxPendingCommandsPerClient);
        }

        [Fact]
        public void SettingsOverridden_AreReadFromTheEnvironment()
        {
            Environment.SetEnvironmentVariable("SERVER_PORT", "9000");
            Environment.SetEnvironmentVariable("DEFAULT_CHARACTER_SLOT", "5");

            ServerConfig.Initialize();

            Assert.Equal(9000, ServerConfig.ServerPort);
            Assert.Equal(5, ServerConfig.DefaultCharacterSlots);
        }

        [Theory]
        [InlineData("SERVER_PORT", "abc")]
        [InlineData("SERVER_PORT", "70000")]
        [InlineData("SERVER_PORT", "0")]
        [InlineData("DEFAULT_CHARACTER_SLOT", "-2")]
        [InlineData("BASE_HEALTH_RES", "1.5")]
        public void InvalidIntegerValues_FallBackToTheDefault(string key, string value)
        {
            Environment.SetEnvironmentVariable(key, value);

            ServerConfig.Initialize();

            Assert.Equal(7777, ServerConfig.ServerPort);
            Assert.Equal(3, ServerConfig.DefaultCharacterSlots);
            Assert.Equal(5, ServerConfig.BaseHealthRes);
        }

        [Theory]
        [InlineData("REGEN_COOLDOWN_SECONDS", "abc")]
        [InlineData("REGEN_COOLDOWN_SECONDS", "-1")]
        [InlineData("REGEN_COOLDOWN_SECONDS", "NaN")]
        [InlineData("COMMAND_BUDGET_MS", "0")]
        public void InvalidDecimalValues_FallBackToTheDefault(string key, string value)
        {
            Environment.SetEnvironmentVariable(key, value);

            ServerConfig.Initialize();

            Assert.Equal(5.0, ServerConfig.RegenCooldownSeconds);
            Assert.Equal(10.0, ServerConfig.CommandBudgetMs);
        }

        [Fact]
        public void Initialize_IsIdempotent_ARemovedValueReturnsToItsDefault()
        {
            Environment.SetEnvironmentVariable("SERVER_PORT", "9000");
            ServerConfig.Initialize();
            Assert.Equal(9000, ServerConfig.ServerPort);

            Environment.SetEnvironmentVariable("SERVER_PORT", null);
            ServerConfig.Initialize();

            Assert.Equal(7777, ServerConfig.ServerPort);
        }

        [Fact]
        public void FindEnvFile_LooksInWorkingDirectory_ItsServerFolder_AndAboveTheExecutable()
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "envtest-" + Guid.NewGuid());
            string server = System.IO.Path.Combine(root, "Server");
            string bin = System.IO.Path.Combine(server, "bin", "Debug", "net8.0");
            string elsewhere = System.IO.Path.Combine(root, "elsewhere");
            System.IO.Directory.CreateDirectory(bin);
            System.IO.Directory.CreateDirectory(elsewhere);
            try
            {
                Assert.Null(ServerConfig.FindEnvFile(elsewhere, elsewhere));

                string envFile = System.IO.Path.Combine(server, ".env");
                System.IO.File.WriteAllText(envFile, "GAME_VERSION=x");

                // run from the repo root (the case that used to ignore Server/.env)
                Assert.Equal(envFile, ServerConfig.FindEnvFile(root, elsewhere));
                // run from the Server folder
                Assert.Equal(envFile, ServerConfig.FindEnvFile(server, elsewhere));
                // run from anywhere, with the executable under Server/bin/...
                Assert.Equal(envFile, ServerConfig.FindEnvFile(elsewhere, bin));
            }
            finally
            {
                System.IO.Directory.Delete(root, true);
            }
        }
    }
}
