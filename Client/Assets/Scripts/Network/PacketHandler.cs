using System;
using System.Collections.Generic;
using UnityEngine;
using Shared.Network;

namespace Client.Network
{
    public static class PacketHandler
    {
        public delegate void PacketAction(Packet packet);
        private static readonly Dictionary<int, PacketAction> _packetHandlers = new();

        public static void Initialize()
        {
            _packetHandlers.Clear();

            // Register handlers
            _packetHandlers.Add((int)OpCode.SignInResponse, Handlers.AuthHandler.HandleAuthResponse);
            _packetHandlers.Add((int)OpCode.SignUpResponse, Handlers.AuthHandler.HandleAuthResponse);
            
            Debug.Log($"[PacketHandler] Initialized with {_packetHandlers.Count} routes.");
        }

        public static void HandlePacket(byte[] data)
        {
            using (Packet packet = new Packet(data))
            {
                int opCode = (int)packet.PacketId;

                if (_packetHandlers.TryGetValue(opCode, out PacketAction handler))
                {
                    handler.Invoke(packet);
                }
                else
                {
                    Debug.LogWarning($"[PacketHandler] Unhandled OpCode: {packet.PacketId} ({opCode})");
                }
            }
        }
    }
}
