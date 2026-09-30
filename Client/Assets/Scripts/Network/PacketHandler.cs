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
            
            _packetHandlers.Add((int)OpCode.CharacterListResponse, Handlers.CharacterHandler.HandleListResponse);
            _packetHandlers.Add((int)OpCode.CharacterCreateResponse, Handlers.CharacterHandler.HandleCreateResponse);
            _packetHandlers.Add((int)OpCode.CharacterDeleteResponse, Handlers.CharacterHandler.HandleDeleteResponse);
            _packetHandlers.Add((int)OpCode.CharacterSelectResponse, Handlers.CharacterHandler.HandleSelectResponse);

            _packetHandlers.Add((int)OpCode.StatsUpdate, Handlers.WorldHandler.HandleStatsUpdate);
            _packetHandlers.Add((int)OpCode.VitalsUpdate, Handlers.WorldHandler.HandleVitalsUpdate);
            _packetHandlers.Add((int)OpCode.EntityPositionUpdate, Handlers.WorldHandler.HandleEntityPositionUpdate);
            _packetHandlers.Add((int)OpCode.EntitySpawn, Handlers.WorldHandler.HandleEntitySpawn);

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
