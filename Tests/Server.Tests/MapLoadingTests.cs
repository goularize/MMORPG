using System;
using System.IO;
using System.Linq;
using Server.World;
using Xunit;

namespace Server.Tests
{
    /// <summary>Map loading must fail fast instead of quietly creating empty, collider-free maps.</summary>
    public class MapLoadingTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "mmorpg-maps-" + Guid.NewGuid().ToString("N"));

        public MapLoadingTests() => Directory.CreateDirectory(_dir);

        public void Dispose() => Directory.Delete(_dir, true);

        private const string Square = """{"MapName":"Test","Colliders":[{"Points":[{"x":0,"y":0},{"x":1,"y":0},{"x":1,"y":1}]}]}""";

        private void WriteMap(string fileName, string json) => File.WriteAllText(Path.Combine(_dir, fileName), json);

        private string[] ErrorsOf(Func<MapManager> create) => Assert.Throws<MapLoadException>(create).Errors.ToArray();

        [Fact]
        public void ValidMaps_LoadWithTheirColliders()
        {
            WriteMap("01_Village.json", Square);
            WriteMap("02_Forest.json", Square);

            var manager = new MapManager(_dir);

            Assert.Equal(new[] { 1, 2 }, manager.ActiveMaps.Keys.OrderBy(k => k));
            Assert.Single(manager.GetMap(1)!.Colliders);
        }

        [Fact]
        public void ShippedMaps_Load()
        {
            var manager = new MapManager();

            Assert.NotEmpty(manager.GetMap(1)!.Colliders);
        }

        [Fact]
        public void MissingDirectory_Fails()
        {
            Assert.Contains("Maps directory not found", ErrorsOf(() => new MapManager(Path.Combine(_dir, "nope"))).Single());
        }

        [Fact]
        public void EmptyDirectory_Fails()
        {
            Assert.Contains("No map files", ErrorsOf(() => new MapManager(_dir)).Single());
        }

        [Fact]
        public void MalformedMap_FailsInsteadOfBecomingAnEmptyWalkableMap()
        {
            WriteMap("01_Village.json", "{ broken");

            Assert.Contains(ErrorsOf(() => new MapManager(_dir)), e => e.StartsWith("01_Village.json: malformed JSON"));
        }

        [Fact]
        public void AllProblemsAreReportedTogether()
        {
            WriteMap("01_Village.json", """{"Colliders":[{"Points":[{"x":0,"y":0},{"x":1,"y":1}]}]}""");
            WriteMap("notanumber.json", Square);
            WriteMap("1_Duplicate.json", Square);
            WriteMap("0001_Again.json", Square);

            var errors = ErrorsOf(() => new MapManager(_dir));

            Assert.Contains(errors, e => e.Contains("collider #0 has 2 point(s)"));
            Assert.Contains(errors, e => e.StartsWith("notanumber.json: the file name must start with the map id"));
            Assert.True(errors.Length >= 3);
        }

        [Fact]
        public void DuplicateMapIds_AreRejected()
        {
            WriteMap("01_A.json", Square);
            WriteMap("1_B.json", Square);

            Assert.Contains(ErrorsOf(() => new MapManager(_dir)), e => e.Contains("map id 1 is already used"));
        }

        [Fact]
        public void SpawnersAndStartPoint_MustReferenceLoadedMaps()
        {
            WriteMap("01_Village.json", Square);
            var manager = new MapManager(_dir);

            var errors = manager.FindMissingMapReferences(
                new[] { new Server.Data.Models.SpawnerTemplate { MapId = 1 }, new Server.Data.Models.SpawnerTemplate { MapId = 7 } },
                startMapId: 3);

            Assert.Contains(errors, e => e.Contains("StartMapId 3"));
            Assert.Contains(errors, e => e.Contains("map 7"));
            Assert.DoesNotContain(errors, e => e.Contains("map 1,"));
        }
    }
}
