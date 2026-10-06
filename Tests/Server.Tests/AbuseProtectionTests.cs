using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Server.Database;
using Server.Database.Models;
using Server.Handlers;
using Server.Network;
using Server.World;
using Server.World.Entities;
using Shared.Constants;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class AbuseProtectionTests : IDisposable
    {
        public AbuseProtectionTests()
        {
            AuthHandler.ResetThrottle();
        }

        public void Dispose()
        {
            AuthHandler.ResetThrottle();
        }

        // --- FailureThrottle / TokenBucket ---

        [Fact]
        public void FailureThrottle_LocksAfterMaxFailures_AndUnlocksAfterLockout()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var throttle = new FailureThrottle(3, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), () => now);

            throttle.RecordFailure("a");
            throttle.RecordFailure("a");
            Assert.False(throttle.IsLocked("a"));
            throttle.RecordFailure("a");
            Assert.True(throttle.IsLocked("a"));
            Assert.False(throttle.IsLocked("b")); // other addresses are unaffected

            now += TimeSpan.FromSeconds(119);
            Assert.True(throttle.IsLocked("a"));
            now += TimeSpan.FromSeconds(2);
            Assert.False(throttle.IsLocked("a"));
        }

        [Fact]
        public void FailureThrottle_FailuresOutsideTheWindowDoNotAccumulate()
        {
            var now = DateTime.UtcNow;
            var throttle = new FailureThrottle(3, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), () => now);

            throttle.RecordFailure("a");
            throttle.RecordFailure("a");
            now += TimeSpan.FromSeconds(61);
            throttle.RecordFailure("a");

            Assert.False(throttle.IsLocked("a"));
        }

        [Fact]
        public void TokenBucket_AllowsBurst_ThenSustainedRate()
        {
            var now = DateTime.UtcNow;
            var bucket = new TokenBucket(3, 1.0, () => now);

            Assert.True(bucket.TryTake());
            Assert.True(bucket.TryTake());
            Assert.True(bucket.TryTake());
            Assert.False(bucket.TryTake());

            now += TimeSpan.FromSeconds(1);
            Assert.True(bucket.TryTake());
            Assert.False(bucket.TryTake());

            now += TimeSpan.FromSeconds(60); // refill never exceeds the burst capacity
            Assert.Equal(3, Enumerable.Range(0, 10).Count(_ => bucket.TryTake()));
        }

        // --- Sign-in throttling ---

        private static void UseFreshDb()
        {
            string name = Guid.NewGuid().ToString();
            AppDbContext.Factory = () =>
            {
                var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);
                ctx.Database.EnsureCreated();
                return ctx;
            };
            using var db = AppDbContext.Factory();
            db.Accounts.Add(new Account { Username = "alice", PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password", 4), CharacterSlots = 3 });
            db.SaveChanges();
        }

        private static (bool ok, string message) SignIn(MockClientConnection client, string password)
        {
            using var w = new Packet(OpCode.SignInRequest);
            w.Write(ServerConfig.GameVersion);
            w.Write("alice");
            w.Write(password);
            using var r = new Packet(w.ToArray());
            AuthHandler.HandleSignInRequest(client, r);
            var resp = client.SentPackets.Last();
            return (resp.ReadBool(), resp.ReadString());
        }

        [Fact]
        public void SignIn_IsLockedAfterRepeatedFailures_EvenWithTheCorrectPassword()
        {
            UseFreshDb();
            var client = new MockClientConnection { AccountId = null };

            for (int i = 0; i < ServerConfig.SignInMaxFailures; i++)
                Assert.Equal("Invalid credentials.", SignIn(client, "wrong-password").message);

            var locked = SignIn(client, "correct-password");
            Assert.False(locked.ok);
            Assert.Contains("Too many failed attempts", locked.message);
            Assert.Null(client.AccountId);
        }

        [Fact]
        public void SuccessfulSignIn_ClearsEarlierFailures()
        {
            UseFreshDb();

            for (int round = 0; round < 3; round++)
            {
                var client = new MockClientConnection { AccountId = null };
                for (int i = 0; i < ServerConfig.SignInMaxFailures - 1; i++) SignIn(client, "wrong-password");
                Assert.True(SignIn(client, "correct-password").ok);
            }
        }

        // --- Chat ---

        private static (MockClientConnection client, Player player) SetupChatter()
        {
            GameLogic.MapMgr.ActiveMaps[1].Players.Clear();
            var client = new MockClientConnection { AccountId = 1, PlayerId = 1 };
            var player = new Player(1, "Alice", client) { Position = new Vector3(0, 0, 0) };
            GameLogic.MapMgr.AddPlayer(player);
            return (client, player);
        }

        private static void Chat(MockClientConnection client, string text)
        {
            using var w = new Packet(OpCode.ChatMessageRequest);
            w.Write((byte)ChatChannel.Global);
            w.Write(text);
            using var r = new Packet(w.ToArray());
            ChatHandler.HandleChatMessage(client, r);
        }

        private static string[] ChatTextsReceived(MockClientConnection client) =>
            client.SentPackets.Select(p => { p.ReadByte(); p.ReadString(); return p.ReadString(); }).ToArray();

        [Fact]
        public void Chat_OverTheLengthCap_IsRefusedWithNotice()
        {
            var (client, _) = SetupChatter();

            Chat(client, new string('a', InputRules.ChatMaxMessageLength + 1));

            var texts = ChatTextsReceived(client);
            Assert.Single(texts);
            Assert.Contains("limited to", texts[0]);
        }

        [Fact]
        public void Chat_AtTheLengthCap_IsDelivered()
        {
            var (client, _) = SetupChatter();
            string message = new string('a', InputRules.ChatMaxMessageLength);

            Chat(client, message);

            Assert.Equal(new[] { message }, ChatTextsReceived(client));
        }

        [Fact]
        public void Chat_Flood_IsDroppedAfterTheBurst_AndNotifiedOnce()
        {
            var (client, _) = SetupChatter();

            for (int i = 0; i < ServerConfig.ChatBurst + 20; i++) Chat(client, $"spam {i}");

            var texts = ChatTextsReceived(client);
            Assert.Equal(ServerConfig.ChatBurst, texts.Count(t => t.StartsWith("spam")));
            Assert.Single(texts, t => t.Contains("too fast"));
        }
    }
}
