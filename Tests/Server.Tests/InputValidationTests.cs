using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Server.Database;
using Server.Database.Models;
using Server.Handlers;
using Shared.Constants;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class InputValidationTests
    {
        [Theory]
        [InlineData("bob", true)]
        [InlineData("Bob_the-2nd", true)]
        [InlineData("ab", false)]
        [InlineData("", false)]
        [InlineData("has space", false)]
        [InlineData("emoji😀name", false)]
        [InlineData("acento_é", false)]
        public void Username_Rules(string username, bool valid) =>
            Assert.Equal(valid, InputRules.ValidateUsername(username) == null);

        [Fact]
        public void Username_TooLong_IsRejected() =>
            Assert.NotNull(InputRules.ValidateUsername(new string('a', InputRules.UsernameMaxLength + 1)));

        [Theory]
        [InlineData("12345678", true)]
        [InlineData("short", false)]
        [InlineData("", false)]
        public void Password_Rules(string password, bool valid) =>
            Assert.Equal(valid, InputRules.ValidatePassword(password) == null);

        [Fact]
        public void Password_OverBCryptLimit_IsRejectedByBytes()
        {
            Assert.Null(InputRules.ValidatePassword(new string('a', InputRules.PasswordMaxBytes)));
            Assert.NotNull(InputRules.ValidatePassword(new string('a', InputRules.PasswordMaxBytes + 1)));
            // 40 two-byte characters = 80 bytes, though only 40 chars
            Assert.NotNull(InputRules.ValidatePassword(new string('é', 40)));
        }

        [Theory]
        [InlineData("Thrall", true)]
        [InlineData("Lady Jaina", true)]
        [InlineData("Bo", false)]
        [InlineData(" Thrall", false)]
        [InlineData("Thrall ", false)]
        [InlineData("Two  Spaces", false)]
        [InlineData("Thr@ll", false)]
        [InlineData("Drop'; --", false)]
        public void CharacterName_Rules(string name, bool valid) =>
            Assert.Equal(valid, InputRules.ValidateCharacterName(name) == null);

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(InputRules.MaxAppearanceId, true)]
        [InlineData(InputRules.MaxAppearanceId + 1, false)]
        [InlineData(-5, false)]
        public void AppearanceId_Rules(int id, bool valid) =>
            Assert.Equal(valid, InputRules.IsValidAppearanceId(id));

        private static void UseFreshDb()
        {
            string name = Guid.NewGuid().ToString();
            AuthHandler.ResetThrottle(); // failures from earlier tests must not lock this address out
            AppDbContext.Factory = () =>
            {
                var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);
                ctx.Database.EnsureCreated();
                return ctx;
            };
        }

        private static (bool ok, string message) SignUp(MockClientConnection client, string user, string pass, string? email = null)
        {
            using var w = new Packet(OpCode.SignUpRequest);
            w.Write(ServerConfig.GameVersion);
            w.Write(user);
            w.Write(pass);
            w.Write(email ?? $"{user.ToLowerInvariant()}@example.com");
            using var r = new Packet(w.ToArray());
            AuthHandler.HandleSignUpRequest(client, r);
            var resp = client.SentPackets.Last();
            return (resp.ReadBool(), resp.ReadString());
        }

        [Fact]
        public void SignUp_WithInvalidInput_CreatesNoAccount()
        {
            UseFreshDb();
            var client = new MockClientConnection { AccountId = null };

            Assert.False(SignUp(client, "ab", "longenough").ok);
            Assert.False(SignUp(client, "valid_name", "short").ok);
            Assert.False(SignUp(client, new string('a', 500), "longenough").ok); // would overflow the DB column

            using var db = AppDbContext.Factory();
            Assert.Empty(db.Accounts);
            Assert.Null(client.AccountId);
        }

        [Fact]
        public void SignUp_WithValidInput_Succeeds_AndDuplicateIgnoringCaseIsRejected()
        {
            UseFreshDb();

            Assert.True(SignUp(new MockClientConnection { AccountId = null }, "Valid_Name", "longenough").ok);
            Assert.False(SignUp(new MockClientConnection { AccountId = null }, "valid_name", "longenough").ok);
        }

        [Theory]
        [InlineData("player@example.com", true)]
        [InlineData("first.last+tag@sub.example.co", true)]
        [InlineData("", false)]
        [InlineData("no-at-sign.example.com", false)]
        [InlineData("@example.com", false)]
        [InlineData("player@", false)]
        [InlineData("player@example", false)]
        [InlineData("player@.example.com", false)]
        [InlineData("player@example..com", false)]
        [InlineData("player@example.com.", false)]
        [InlineData("two@@example.com", false)]
        [InlineData("has space@example.com", false)]
        [InlineData("acento@exámple.com", false)]
        public void Email_Rules(string email, bool valid) =>
            Assert.Equal(valid, InputRules.ValidateEmail(email) == null);

        [Fact]
        public void Email_TooLong_IsRejected() =>
            Assert.NotNull(InputRules.ValidateEmail(new string('a', InputRules.EmailMaxLength) + "@example.com"));

        [Fact]
        public void Email_Normalize_TrimsAndLowerCases() =>
            Assert.Equal("player@example.com", InputRules.NormalizeEmail("  Player@Example.COM "));

        [Fact]
        public void SignUp_WithInvalidEmail_CreatesNoAccount()
        {
            UseFreshDb();
            var client = new MockClientConnection { AccountId = null };

            Assert.False(SignUp(client, "valid_name", "longenough", "not-an-email").ok);
            Assert.False(SignUp(client, "valid_name", "longenough", "").ok);

            using var db = AppDbContext.Factory();
            Assert.Empty(db.Accounts);
        }

        [Fact]
        public void SignUp_StoresNormalizedEmail_AndRejectsDuplicateEmailIgnoringCase()
        {
            UseFreshDb();

            Assert.True(SignUp(new MockClientConnection { AccountId = null }, "first_user", "longenough", "  Shared@Example.com ").ok);
            var (ok, message) = SignUp(new MockClientConnection { AccountId = null }, "second_user", "longenough", "shared@example.com");

            Assert.False(ok);
            Assert.Equal("Email is already registered.", message);
            using var db = AppDbContext.Factory();
            var account = Assert.Single(db.Accounts);
            Assert.Equal("shared@example.com", account.Email);
        }

        private static string CreateCharacter(string name, int appearance)
        {
            var client = new MockClientConnection();
            using var w = new Packet(OpCode.CharacterCreateRequest);
            w.Write(name);
            w.Write(appearance);
            using var r = new Packet(w.ToArray());
            CharacterHandler.HandleCreateRequest(client, r);
            var resp = client.SentPackets.Single();
            return resp.ReadBool() ? "ok" : resp.ReadString();
        }

        [Fact]
        public void CharacterCreate_ValidatesNameAndAppearance_AndTrims()
        {
            UseFreshDb();
            using (var db = AppDbContext.Factory())
            {
                db.Accounts.Add(new Account { Id = 1, Username = "test", PasswordHash = "hash", CharacterSlots = 5 });
                db.SaveChanges();
            }

            Assert.NotEqual("ok", CreateCharacter("x", 1));
            Assert.NotEqual("ok", CreateCharacter(new string('a', 200), 1));
            Assert.NotEqual("ok", CreateCharacter("Bad@Name", 1));
            Assert.Equal("Invalid appearance.", CreateCharacter("Thrall", 9999));
            Assert.Equal("ok", CreateCharacter("  Thrall  ", 1));

            using var check = AppDbContext.Factory();
            Assert.Equal("Thrall", check.Characters.Single().Name);
        }

        private static void WriteCreationData(string dir, string creationJson)
        {
            File.WriteAllText(Path.Combine(dir, "CharacterCreation.json"), creationJson);
        }

        [Fact]
        public void CharacterCreate_AppliesDataDrivenStartingKit_InOneSave()
        {
            UseFreshDb();
            string dir = Path.Combine(Path.GetTempPath(), "mmorpg-cc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Data"), "*.json"))
                    File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
                WriteCreationData(dir, """{"StartMapId":1,"StartX":3,"Health":77,"Strength":12,"Gold":321,"StarterItems":[{"TemplateId":2001,"Quantity":4},{"TemplateId":1001}]}""");
                Server.Data.DataManager.Initialize(dir);

                using (var db = AppDbContext.Factory())
                {
                    db.Accounts.Add(new Account { Id = 1, Username = "test", PasswordHash = "hash", CharacterSlots = 5 });
                    db.SaveChanges();
                }
                Assert.Equal("ok", CreateCharacter("Thrall", 1));

                using var check = AppDbContext.Factory();
                var c = check.Characters.Include(x => x.InventoryItems).Single();
                Assert.Equal((77, 12, 321L, 3f), (c.Health, c.Strength, c.Gold, c.X));
                Assert.Equal(3f, c.BindX);
                Assert.Equal(new[] { (0, 2001, 4), (1, 1001, 1) },
                    c.InventoryItems.OrderBy(i => i.SlotIndex).Select(i => (i.SlotIndex, i.TemplateId, i.Quantity)).ToArray());
            }
            finally
            {
                Directory.Delete(dir, true);
                Server.Data.DataManager.Initialize();
            }
        }

        [Fact]
        public void CharacterCreate_WhenAStarterItemCannotBeBuilt_CreatesNothing()
        {
            UseFreshDb();
            Server.Data.DataManager.Initialize();
            var removed = Server.Data.DataManager.Items[1001];
            Server.Data.DataManager.Items.Remove(1001); // simulate the item vanishing after validation
            try
            {
                using (var db = AppDbContext.Factory())
                {
                    db.Accounts.Add(new Account { Id = 1, Username = "test", PasswordHash = "hash", CharacterSlots = 5 });
                    db.SaveChanges();
                }

                Assert.NotEqual("ok", CreateCharacter("Thrall", 1));

                using var check = AppDbContext.Factory();
                Assert.Empty(check.Characters);
                Assert.Empty(check.CharacterItems);
            }
            finally
            {
                Server.Data.DataManager.Items[1001] = removed;
            }
        }
    }
}
