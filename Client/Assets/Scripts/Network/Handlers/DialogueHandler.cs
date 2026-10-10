using System;
using UnityEngine;
using Shared.Enums;
using Shared.Network;

namespace Client.Network.Handlers
{
    /// <summary>One selectable line of a dialogue window, exactly as the server offered it.</summary>
    public readonly struct DialogueOption
    {
        public readonly int OptionId;
        public readonly string Label;
        public readonly InteractAction Action;

        public DialogueOption(int optionId, string label, InteractAction action)
        {
            OptionId = optionId;
            Label = label;
            Action = action;
        }
    }

    /// <summary>What the server wants the dialogue window to show (DialogueOpen).</summary>
    public sealed class DialogueData
    {
        public int NpcId;
        public string NpcName;
        public string Text;
        public DialogueOption[] Options;
    }

    /// <summary>
    /// Talking to NPCs. The client only asks to interact, answers with an option id the server offered, and tells the
    /// server when the window was closed; every rule (conditions, quests, bind point) lives on the server. The UI
    /// subscribes to the static events, so this class stays plain C#.
    /// </summary>
    public static class DialogueHandler
    {
        /// <summary>The conversation currently open (null when there is none).</summary>
        public static DialogueData Current { get; private set; }

        public static event Action<DialogueData> OnDialogueOpened;
        public static event Action<int, DialogueCloseReason> OnDialogueClosed;

        /// <summary>A failed interaction (or an interaction the server does not implement yet).</summary>
        public static event Action<int, InteractOutcome, InteractAction> OnInteractFailed;

        public static event Action<bool, int, Vector3> OnBindPointSet;

        /// <summary>Back to no conversation (called when returning to character select).</summary>
        public static void ResetSession()
        {
            Current = null;
        }

        public static void RequestInteract(int targetId)
        {
            using (Packet packet = new Packet(OpCode.EntityInteractRequest))
            {
                packet.Write(targetId);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        public static void Choose(int npcId, int optionId)
        {
            using (Packet packet = new Packet(OpCode.DialogueChoose))
            {
                packet.Write(npcId);
                packet.Write(optionId);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        /// <summary>The player closed the window: tell the server so it drops the conversation.</summary>
        public static void CloseFromClient(int npcId)
        {
            Current = null;
            using (Packet packet = new Packet(OpCode.DialogueClose))
            {
                packet.Write(npcId);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        public static void HandleDialogueOpen(Packet packet)
        {
            var data = new DialogueData
            {
                NpcId = packet.ReadInt(),
                NpcName = packet.ReadString(),
                Text = packet.ReadString()
            };

            int count = packet.ReadByte();
            data.Options = new DialogueOption[count];
            for (int i = 0; i < count; i++)
            {
                int optionId = packet.ReadInt();
                string label = packet.ReadString();
                var action = (InteractAction)packet.ReadByte();
                data.Options[i] = new DialogueOption(optionId, label, action);
            }

            Current = data;
            OnDialogueOpened?.Invoke(data);
        }

        public static void HandleDialogueClose(Packet packet)
        {
            int npcId = packet.ReadInt();
            var reason = (DialogueCloseReason)packet.ReadByte();

            Current = null;
            OnDialogueClosed?.Invoke(npcId, reason);
        }

        public static void HandleInteractResponse(Packet packet)
        {
            int targetId = packet.ReadInt();
            var outcome = (InteractOutcome)packet.ReadByte();
            var action = (InteractAction)packet.ReadByte();

            OnInteractFailed?.Invoke(targetId, outcome, action);
        }

        public static void HandleBindPointResponse(Packet packet)
        {
            bool success = packet.ReadBool();
            // Always carries the current bind point, also when the request was rejected
            int mapId = packet.ReadInt();
            var pos = packet.ReadVector3();
            var position = new Vector3(pos.X, pos.Y, pos.Z);

            OnBindPointSet?.Invoke(success, mapId, position);
        }
    }
}
