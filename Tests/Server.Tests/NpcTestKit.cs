using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Data.Models;
using Server.Database;
using Server.Handlers;
using Server.Network;
using Server.World;
using Server.World.Entities;
using Shared.Enums;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    /// <summary>A connection that keeps the raw bytes of everything sent, so a test can parse the same packet any number of times.</summary>
    public sealed class RecordingClient : IClientConnection
    {
        public RecordingClient(int playerId) { PlayerId = playerId; }

        public int Id => 1;
        public int? AccountId { get; set; } = 1;
        public int? PlayerId { get; set; }
        public bool IsConnected => true;
        public List<byte[]> Raw { get; } = new();

        public void Send(Packet packet) => Raw.Add(packet.ToArray());
        public void Disconnect() { }

        public IEnumerable<Packet> Of(OpCode opCode) =>
            Raw.Where(b => BitConverter.ToUInt16(b, 2) == (ushort)opCode).Select(b => new Packet(b));

        public int Count(OpCode opCode) => Of(opCode).Count();

        public DialogueView LastDialogue()
        {
            using var p = Of(OpCode.DialogueOpen).Last();
            int npcId = p.ReadInt();
            string npcName = p.ReadString();
            string text = p.ReadString();
            int count = p.ReadByte();
            var options = new List<(int Id, string Label, InteractAction Action)>();
            for (int i = 0; i < count; i++)
            {
                int id = p.ReadInt();
                string label = p.ReadString();
                options.Add((id, label, (InteractAction)p.ReadByte()));
            }
            return new DialogueView(npcId, npcName, text, options);
        }

        public DialogueCloseReason? LastClose()
        {
            var packets = Of(OpCode.DialogueClose).ToList();
            if (packets.Count == 0) return null;
            packets.Last().ReadInt();
            return (DialogueCloseReason)packets.Last().ReadByte();
        }

        public (InteractOutcome Outcome, InteractAction Action)? LastInteractReply()
        {
            var packets = Of(OpCode.EntityInteractResponse).ToList();
            if (packets.Count == 0) return null;
            var p = packets.Last();
            p.ReadInt();
            return ((InteractOutcome)p.ReadByte(), (InteractAction)p.ReadByte());
        }

        /// <summary>The last QuestUpdate for the quest: state and counters.</summary>
        public (QuestState State, int[] Counters)? LastQuestUpdate(int questId)
        {
            var entry = LastQuestEntry(questId);
            return entry == null ? null : (entry.State, entry.Objectives.Select(o => o.Current).ToArray());
        }

        /// <summary>The last QuestUpdate for the quest with its name and objective texts.</summary>
        public QuestEntryView? LastQuestEntry(int questId)
        {
            foreach (var p in Of(OpCode.QuestUpdate).Reverse())
            {
                if (p.ReadInt() != questId) continue;
                return ReadQuestEntry(p);
            }
            return null;
        }

        /// <summary>Reads one QuestEntry as written by QuestHandler (state, name, objectives).</summary>
        public static QuestEntryView ReadQuestEntry(Packet p)
        {
            var state = (QuestState)p.ReadByte();
            string name = p.ReadString();
            int count = p.ReadByte();
            var objectives = new List<(string Text, int Current, int Required)>();
            for (int i = 0; i < count; i++) objectives.Add((p.ReadString(), p.ReadInt(), p.ReadInt()));
            return new QuestEntryView(state, name, objectives);
        }
    }

    public sealed record QuestEntryView(QuestState State, string Name, IReadOnlyList<(string Text, int Current, int Required)> Objectives);

    public sealed record DialogueView(int NpcId, string NpcName, string Text, IReadOnlyList<(int Id, string Label, InteractAction Action)> Options)
    {
        public bool Has(string labelPart) => Options.Any(o => o.Label.Contains(labelPart, StringComparison.OrdinalIgnoreCase));

        public int IdOf(string labelPart) =>
            Options.First(o => o.Label.Contains(labelPart, StringComparison.OrdinalIgnoreCase)).Id;
    }

    /// <summary>
    /// A small world for dialogue and quest tests: map 1, an in-memory database, the shipped static data (restored
    /// afterwards, so tests can add templates freely) and helpers to place players and NPCs and drive the handlers.
    /// </summary>
    public abstract class NpcWorldTestBase : IDisposable
    {
        private static int _nextId = 8000;
        private readonly string _dbName = Guid.NewGuid().ToString();

        protected AppDbContext Db()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
            var ctx = new AppDbContext(options);
            ctx.Database.EnsureCreated();
            return ctx;
        }

        protected MapInstance Map => GameLogic.MapMgr.GetMap(1)!;

        protected NpcWorldTestBase()
        {
            AppDbContext.Factory = Db;
            DataManager.Initialize();
            GameLogic.MapMgr.ActiveMaps.Clear();
            GameLogic.MapMgr.ActiveMaps.TryAdd(1, new MapInstance(1));
        }

        public void Dispose() => DataManager.Initialize(); // drop the templates a test added

        protected (RecordingClient client, Player player) AddPlayer(string name = "Hero", int level = 1, float x = 1f)
        {
            int id = _nextId++;
            var client = new RecordingClient(id);
            var player = new Player(id, name, client)
            {
                MapId = 1,
                Level = level,
                Position = new Vector3(x, 0, 0),
                Health = 100,
                MaxHealth = 100
            };
            using (var db = Db())
            {
                db.Characters.Add(new Server.Database.Models.Character { Id = id, AccountId = 1, Name = name + id });
                db.SaveChanges();
            }
            Map.AddPlayer(player);
            return (client, player);
        }

        protected NPC AddNpc(int templateId, string name = "Npc", float x = 0f)
        {
            var npc = new NPC(_nextId++, name)
            {
                TemplateId = templateId,
                BehaviorType = DataManager.Npcs.TryGetValue(templateId, out var t) ? t.BehaviorType : MobBehaviorType.Passive,
                Position = new Vector3(x, 0, 0),
                SpawnPosition = new Vector3(x, 0, 0)
            };
            npc.CalculateDerivedStats();
            Map.AddNPC(npc);
            return npc;
        }

        protected static void Interact(RecordingClient client, Player player, NPC npc)
        {
            player.KnownEntities.Add(npc.Id);
            using var write = new Packet(OpCode.EntityInteractRequest);
            write.Write(npc.Id);
            InteractHandler.HandleInteractRequest(client, new Packet(write.ToArray()));
        }

        protected static void Choose(RecordingClient client, NPC npc, int optionId)
        {
            using var write = new Packet(OpCode.DialogueChoose);
            write.Write(npc.Id);
            write.Write(optionId);
            DialogueHandler.HandleChoose(client, new Packet(write.ToArray()));
        }

        /// <summary>Picks the option whose label contains the text on the screen currently open.</summary>
        protected static void Pick(RecordingClient client, NPC npc, string labelPart) =>
            Choose(client, npc, client.LastDialogue().IdOf(labelPart));

        protected static void Kill(NPC npc, Player killer) => npc.Die(GameLogic.MapMgr.GetMap(killer.MapId), killer);

        protected static void KillSlimes(Player killer, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var slime = new NPC(_nextId++, "Slime") { TemplateId = 101, Position = killer.Position };
                slime.CalculateDerivedStats();
                Kill(slime, killer);
            }
        }

        protected static void Give(Player player, int itemTemplateId, int quantity) =>
            Assert.True(InventoryOps.AddToBackpack(player, itemTemplateId, quantity));

        protected static int Held(Player player, int itemTemplateId) => InventoryOps.CountInBackpack(player, itemTemplateId);

        /// <summary>Fills the backpack with single-slot junk until no slot is free.</summary>
        protected static void FillBackpack(Player player)
        {
            while (InventoryHandler.FindFirstEmptySlot(player, 0) is int slot)
            {
                var item = ItemFactory.CreateItem(1001, player.Id)!; // an Iron Broadsword: never stacks
                item.BagIndex = 0;
                item.SlotIndex = slot;
                player.Inventory.Add(item);
            }
        }

        protected static int NewNpcTemplate(string dialogueId, int templateId, string name = "Test NPC")
        {
            DataManager.Npcs[templateId] = new NpcTemplate
            {
                TemplateId = templateId,
                Name = name,
                Type = "Friendly",
                LevelRange = new[] { 1, 1 },
                StatVarianceRange = new[] { 1f, 1f },
                DialogueId = dialogueId
            };
            return templateId;
        }
    }
}
