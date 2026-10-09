using System.Collections.Generic;
using System.Linq;
using Server.Data;
using Server.Data.Models;
using Server.Handlers;
using Server.World;
using Server.World.Entities;
using Shared.Enums;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class InnkeeperBindTests
    {
        private const int InnkeeperTemplate = 201;

        public InnkeeperBindTests()
        {
            // Own the shipped NPC templates instead of relying on whichever test class ran before
            DataManager.Initialize();
            GameLogic.MapMgr.ActiveMaps.Clear();
            GameLogic.MapMgr.ActiveMaps.TryAdd(1, new MapInstance(1));
        }

        private static (MockClientConnection client, Player player, NPC innkeeper) Setup(float playerX, int templateId = InnkeeperTemplate)
        {
            var map = GameLogic.MapMgr.GetMap(1)!;
            var client = new MockClientConnection { PlayerId = 701 };
            var player = new Player(701, "Wanderer", client) { MapId = 1, Position = new Vector3(playerX, 0, 0) };
            player.BindMapId = 1;
            player.BindPosition = new Vector3(100, 0, 100);
            map.AddPlayer(player);

            var npc = new NPC(70001, "Innkeeper")
            {
                TemplateId = templateId,
                BehaviorType = MobBehaviorType.Friendly,
                Position = new Vector3(0, 0, 0)
            };
            npc.CalculateDerivedStats();
            map.AddNPC(npc);
            player.KnownEntities.Add(npc.Id);
            return (client, player, npc);
        }

        private static (bool success, Vector3 bindPosition) ReadBindResponse(MockClientConnection client)
        {
            var packet = client.SentPackets.Last(p => p.PacketId == OpCode.SetBindPointResponse);
            bool success = packet.ReadBool();
            packet.ReadInt();
            return (success, packet.ReadVector3());
        }

        private static (InteractOutcome outcome, InteractAction action) ReadInteractResponse(MockClientConnection client)
        {
            var packet = client.SentPackets.Last(p => p.PacketId == OpCode.EntityInteractResponse);
            packet.ReadInt();
            return ((InteractOutcome)packet.ReadByte(), (InteractAction)packet.ReadByte());
        }

        private static Packet Interact(int targetId)
        {
            var packet = new Packet(OpCode.EntityInteractRequest);
            packet.Write(targetId);
            return new Packet(packet.ToArray());
        }

        [Fact]
        public void ShippedInnkeeperTemplate_OffersBindPoint()
        {
            Assert.True(DataManager.Npcs[InnkeeperTemplate].OffersAction(InteractAction.BindPoint));
        }

        [Fact]
        public void BindRequest_NearInnkeeper_BindsToCurrentPosition()
        {
            var (client, player, _) = Setup(playerX: 1);

            BindHandler.HandleSetBindPointRequest(client, new Packet(OpCode.SetBindPointRequest));

            var (success, position) = ReadBindResponse(client);
            Assert.True(success);
            Assert.Equal(new Vector3(1, 0, 0), position);
            Assert.Equal(player.Position, player.BindPosition);
        }

        [Fact]
        public void BindRequest_FarFromInnkeeper_IsRejectedAndKeepsOldBind()
        {
            var (client, player, _) = Setup(playerX: Shared.Constants.GameRules.InteractRange + 5);

            BindHandler.HandleSetBindPointRequest(client, new Packet(OpCode.SetBindPointRequest));

            var (success, position) = ReadBindResponse(client);
            Assert.False(success);
            Assert.Equal(new Vector3(100, 0, 100), position);
            Assert.Equal(new Vector3(100, 0, 100), player.BindPosition);
        }

        [Fact]
        public void BindRequest_NearNpcWithoutBindInteraction_IsRejected()
        {
            var (client, player, _) = Setup(playerX: 1, templateId: 200); // the blacksmith

            BindHandler.HandleSetBindPointRequest(client, new Packet(OpCode.SetBindPointRequest));

            Assert.False(ReadBindResponse(client).success);
            Assert.Equal(new Vector3(100, 0, 100), player.BindPosition);
        }

        [Fact]
        public void BindRequest_DeadInnkeeper_IsRejected()
        {
            var (client, _, innkeeper) = Setup(playerX: 1);
            innkeeper.Health = 0;

            BindHandler.HandleSetBindPointRequest(client, new Packet(OpCode.SetBindPointRequest));

            Assert.False(ReadBindResponse(client).success);
        }

        private static Packet Choose(int npcId, int optionId)
        {
            var packet = new Packet(OpCode.DialogueChoose);
            packet.Write(npcId);
            packet.Write(optionId);
            return new Packet(packet.ToArray());
        }

        /// <summary>Reads the last DialogueOpen: the option ids and the actions they perform.</summary>
        private static List<(int optionId, InteractAction action)> ReadOfferedOptions(MockClientConnection client)
        {
            var packet = client.SentPackets.Last(p => p.PacketId == OpCode.DialogueOpen);
            packet.ReadInt();
            packet.ReadString();
            packet.ReadString();
            int count = packet.ReadByte();
            var options = new List<(int, InteractAction)>();
            for (int i = 0; i < count; i++)
            {
                int optionId = packet.ReadInt();
                packet.ReadString();
                options.Add((optionId, (InteractAction)packet.ReadByte()));
            }
            return options;
        }

        [Fact]
        public void Interact_WithInnkeeper_OpensADialogue_AndPickingBindBinds()
        {
            var (client, player, innkeeper) = Setup(playerX: 1);

            InteractHandler.HandleInteractRequest(client, Interact(innkeeper.Id));
            var bind = ReadOfferedOptions(client).Single(o => o.action == InteractAction.BindPoint);
            Assert.Equal(new Vector3(100, 0, 100), player.BindPosition); // opening a conversation binds nothing

            DialogueHandler.HandleChoose(client, Choose(innkeeper.Id, bind.optionId));

            Assert.True(ReadBindResponse(client).success);
            Assert.Equal(player.Position, player.BindPosition);
            Assert.Null(player.Dialogue); // the conversation ends after binding
        }

        [Fact]
        public void Interact_TooFar_ReportsTooFarAndDoesNotBind()
        {
            var (client, player, innkeeper) = Setup(playerX: Shared.Constants.GameRules.InteractRange + 5);

            InteractHandler.HandleInteractRequest(client, Interact(innkeeper.Id));

            Assert.Equal(InteractOutcome.TooFar, ReadInteractResponse(client).outcome);
            Assert.Equal(new Vector3(100, 0, 100), player.BindPosition);
        }

        [Fact]
        public void Interact_WithUnknownEntity_ReportsNotFound()
        {
            var (client, _, _) = Setup(playerX: 1);

            InteractHandler.HandleInteractRequest(client, Interact(999999));

            Assert.Equal(InteractOutcome.NotFound, ReadInteractResponse(client).outcome);
        }

        [Fact]
        public void Interact_WithDeadTarget_ReportsTargetDead()
        {
            var (client, _, innkeeper) = Setup(playerX: 1);
            innkeeper.Health = 0;

            InteractHandler.HandleInteractRequest(client, Interact(innkeeper.Id));

            Assert.Equal(InteractOutcome.TargetDead, ReadInteractResponse(client).outcome);
        }

        [Fact]
        public void Interact_WithShopNpc_OfferingTheShopReportsNotAvailableUntilVendorsExist()
        {
            var (client, _, blacksmith) = Setup(playerX: 1, templateId: 200);

            InteractHandler.HandleInteractRequest(client, Interact(blacksmith.Id));
            var shop = ReadOfferedOptions(client).First(o => o.action == InteractAction.OpenShop);
            DialogueHandler.HandleChoose(client, Choose(blacksmith.Id, shop.optionId));

            Assert.Equal((InteractOutcome.NotAvailable, InteractAction.OpenShop), ReadInteractResponse(client));
        }

        [Fact]
        public void Interact_WithNpcThatOffersNothing_ReportsNothingToDo()
        {
            var (client, _, mob) = Setup(playerX: 1, templateId: 101); // slime

            InteractHandler.HandleInteractRequest(client, Interact(mob.Id));

            Assert.Equal(InteractOutcome.NothingToDo, ReadInteractResponse(client).outcome);
        }
    }
}
