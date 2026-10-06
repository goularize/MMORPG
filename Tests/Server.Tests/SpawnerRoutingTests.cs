using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Server.Data.Models;
using Server.World;
using Xunit;

namespace Server.Tests
{
    public class SpawnerRoutingTests
    {
        private static MapManager SetupMaps(params int[] mapIds)
        {
            var manager = new MapManager();
            foreach (int id in mapIds)
            {
                manager.ActiveMaps[id] = new MapInstance(id);
            }
            return manager;
        }

        private static Dictionary<int, NpcTemplate> Templates() => new()
        {
            [101] = new NpcTemplate { TemplateId = 101, Name = "Boar" },
            [102] = new NpcTemplate { TemplateId = 102, Name = "Wolf" }
        };

        [Fact]
        public void SpawnSpawners_RoutesEachSpawnerToItsOwnMap()
        {
            var manager = SetupMaps(1, 2);
            var spawners = new List<SpawnerTemplate>
            {
                new() { TemplateId = 101, MapId = 1, Amount = 2 },
                new() { TemplateId = 102, MapId = 2, Amount = 3 }
            };

            int spawned = manager.SpawnSpawners(spawners, Templates());

            Assert.Equal(5, spawned);
            Assert.Equal(2, manager.ActiveMaps[1].NPCs.Count);
            Assert.Equal(3, manager.ActiveMaps[2].NPCs.Count);
            Assert.All(manager.ActiveMaps[1].NPCs.Values, n => { Assert.Equal("Boar", n.Name); Assert.Equal(1, n.MapId); });
            Assert.All(manager.ActiveMaps[2].NPCs.Values, n => { Assert.Equal("Wolf", n.Name); Assert.Equal(2, n.MapId); });
        }

        [Fact]
        public void SpawnSpawners_UnknownMap_IsSkippedAndNotFallenBackToMap1()
        {
            var manager = SetupMaps(1);
            var spawners = new List<SpawnerTemplate> { new() { TemplateId = 101, MapId = 99, Amount = 2 } };

            int spawned = manager.SpawnSpawners(spawners, Templates());

            Assert.Equal(0, spawned);
            Assert.Empty(manager.ActiveMaps[1].NPCs);
        }

        [Fact]
        public void SpawnSpawners_UnknownTemplate_IsSkipped()
        {
            var manager = SetupMaps(1);
            var spawners = new List<SpawnerTemplate> { new() { TemplateId = 999, MapId = 1, Amount = 2 } };

            Assert.Equal(0, manager.SpawnSpawners(spawners, Templates()));
        }

        [Fact]
        public void SpawnerTemplate_JsonWithoutMapId_DefaultsToMap1()
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var legacy = JsonSerializer.Deserialize<List<SpawnerTemplate>>("[{\"TemplateId\":101,\"Amount\":1}]", options)!;
            var routed = JsonSerializer.Deserialize<List<SpawnerTemplate>>("[{\"TemplateId\":101,\"MapId\":2,\"Amount\":1}]", options)!;

            Assert.Equal(1, legacy.Single().MapId);
            Assert.Equal(2, routed.Single().MapId);
        }
    }
}
