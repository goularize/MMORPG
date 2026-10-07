using System;
using System.IO;
using System.Linq;
using Server.Data;
using Xunit;

namespace Server.Tests
{
    /// <summary>Startup loading and validation of the static data tables, using throwaway data directories.</summary>
    public class DataManagerTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "mmorpg-data-" + Guid.NewGuid().ToString("N"));

        public DataManagerTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            Directory.Delete(_dir, true);
            DataManager.Initialize(); // leave the real tables in place for other tests
        }

        private const string ValidItems = """[{"TemplateId":1,"Name":"Sword","Type":"Equipment"},{"TemplateId":2,"Name":"Ore","Type":"Material","MaxStack":20}]""";
        private const string ValidNpcs = """[{"TemplateId":10,"Name":"Slime","Behavior":"Aggressive"}]""";
        private const string ValidLoot = """[{"NpcTemplateId":10,"Entries":[{"ItemTemplateId":2,"DropChance":0.5}]}]""";
        private const string ValidRecipes = """[{"RecipeId":1,"ResultItemTemplateId":1,"Ingredients":[{"ItemTemplateId":2,"Quantity":3}]}]""";
        private const string ValidSpawners = """[{"TemplateId":10,"MapId":1,"Amount":2,"Radius":5}]""";

        private const string ValidCreation = """{"StartMapId":1,"Gold":50,"StarterItems":[{"TemplateId":1,"Quantity":1},{"TemplateId":2,"Quantity":5}]}""";

        private void Write(string items = ValidItems, string npcs = ValidNpcs, string loot = ValidLoot, string recipes = ValidRecipes, string spawners = ValidSpawners, string creation = ValidCreation)
        {
            File.WriteAllText(Path.Combine(_dir, "Items.json"), items);
            File.WriteAllText(Path.Combine(_dir, "Npcs.json"), npcs);
            File.WriteAllText(Path.Combine(_dir, "LootTables.json"), loot);
            File.WriteAllText(Path.Combine(_dir, "Recipes.json"), recipes);
            File.WriteAllText(Path.Combine(_dir, "Spawners.json"), spawners);
            File.WriteAllText(Path.Combine(_dir, "CharacterCreation.json"), creation);
        }

        private string[] ErrorsOf(Action load) => Assert.Throws<DataLoadException>(load).Errors.ToArray();

        [Fact]
        public void ValidData_Loads()
        {
            Write();

            DataManager.Initialize(_dir);

            Assert.Equal(2, DataManager.Items.Count);
            Assert.Single(DataManager.Npcs);
            Assert.Single(DataManager.Spawners);
            Assert.Equal(20, DataManager.Items[2].MaxStack);
        }

        [Fact]
        public void ShippedData_PassesValidation() => DataManager.Initialize();

        [Fact]
        public void MalformedJson_FailsFast_AndKeepsPreviousTables()
        {
            Write();
            DataManager.Initialize(_dir);
            Write(items: "{ not json");

            var errors = ErrorsOf(() => DataManager.Initialize(_dir));

            Assert.Contains(errors, e => e.StartsWith("Items.json: malformed JSON"));
            Assert.Equal(2, DataManager.Items.Count); // not replaced by empty tables
        }

        [Fact]
        public void MissingFile_IsAnError()
        {
            Write();
            File.Delete(Path.Combine(_dir, "Npcs.json"));

            Assert.Contains(ErrorsOf(() => DataManager.Initialize(_dir)), e => e.StartsWith("Npcs.json: file not found"));
        }

        [Fact]
        public void DuplicateIds_AreReported()
        {
            Write(items: """[{"TemplateId":1,"Name":"A","Type":"Material"},{"TemplateId":1,"Name":"B","Type":"Material"}]""");

            Assert.Contains(ErrorsOf(() => DataManager.Initialize(_dir)), e => e.Contains("TemplateId 1 is defined 2 times"));
        }

        [Fact]
        public void DanglingReferences_AreAllReportedTogether()
        {
            Write(
                loot: """[{"NpcTemplateId":10,"Entries":[{"ItemTemplateId":999}]}]""",
                recipes: """[{"RecipeId":1,"ResultItemTemplateId":888,"Ingredients":[{"ItemTemplateId":777}]}]""",
                spawners: """[{"TemplateId":555,"Amount":1}]""");

            var errors = ErrorsOf(() => DataManager.Initialize(_dir));

            Assert.Contains(errors, e => e.Contains("unknown item 999"));
            Assert.Contains(errors, e => e.Contains("unknown item 888"));
            Assert.Contains(errors, e => e.Contains("unknown item 777"));
            Assert.Contains(errors, e => e.Contains("unknown NPC template 555"));
        }

        [Fact]
        public void UnknownNpcType_IsAnError()
        {
            Write(npcs: """[{"TemplateId":10,"Name":"Slime","Type":"Frendly","Behavior":"Passive"}]""");

            Assert.Contains(ErrorsOf(() => DataManager.Initialize(_dir)), e => e.Contains("Type 'Frendly'"));
        }

        [Fact]
        public void UnparsableBehavior_IsAnError_InsteadOfFallingBackToPassive()
        {
            Write(npcs: """[{"TemplateId":10,"Name":"Slime","Behavior":"Agressive"}]""");

            Assert.Contains(ErrorsOf(() => DataManager.Initialize(_dir)), e => e.Contains("Behavior 'Agressive'"));
        }

        [Fact]
        public void BadValues_AreReported()
        {
            Write(
                items: """[{"TemplateId":1,"Name":"","Type":"Unknown","MaxStack":0},{"TemplateId":2,"Name":"Ore","Type":"Material"}]""",
                loot: """[{"NpcTemplateId":10,"Entries":[{"ItemTemplateId":2,"DropChance":1.5,"MinQuantity":5,"MaxQuantity":1}]}]""");

            var errors = ErrorsOf(() => DataManager.Initialize(_dir));

            Assert.Contains(errors, e => e.Contains("Name is empty"));
            Assert.Contains(errors, e => e.Contains("MaxStack must be at least 1"));
            Assert.Contains(errors, e => e.Contains("DropChance must be between 0 and 1"));
            Assert.Contains(errors, e => e.Contains("quantity range"));
        }

        [Fact]
        public void LootTableWithoutNpc_IsOnlyAWarning()
        {
            Write(loot: """[{"NpcTemplateId":4242,"Entries":[]}]""");

            DataManager.Initialize(_dir);

            Assert.True(DataManager.LootTables.ContainsKey(4242));
        }

        [Fact]
        public void ReinitializingDoesNotDuplicateSpawners()
        {
            Write();

            DataManager.Initialize(_dir);
            DataManager.Initialize(_dir);

            Assert.Single(DataManager.Spawners);
        }

        [Fact]
        public void CharacterCreation_StarterItemProblems_AreReported()
        {
            Write(creation: """{"StarterItems":[{"TemplateId":999},{"TemplateId":2,"Quantity":21},{"TemplateId":1,"Quantity":0}]}""");

            var errors = ErrorsOf(() => DataManager.Initialize(_dir));

            Assert.Contains(errors, e => e.Contains("unknown item 999"));
            Assert.Contains(errors, e => e.Contains("starter item 2 quantity"));
            Assert.Contains(errors, e => e.Contains("starter item 1 quantity"));
        }

        [Fact]
        public void CharacterCreation_LoadsValues()
        {
            Write();

            DataManager.Initialize(_dir);

            Assert.Equal(50, DataManager.CharacterCreation.Gold);
            Assert.Equal(2, DataManager.CharacterCreation.StarterItems.Count);
        }
    }
}
