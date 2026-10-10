using System;
using System.Collections.Generic;
using Shared.Enums;
using Shared.Network;

namespace Client.Network.Handlers
{
    /// <summary>
    /// The "!" / "?" over NPCs. The server decides which marker an NPC has for this player and sends it with the
    /// NPC's spawn and whenever it changes (EntityMarker); the client only keeps and shows it.
    /// </summary>
    public static class MarkerHandler
    {
        private static readonly Dictionary<int, EntityMarker> _markers = new();

        /// <summary>An entity's marker changed (None clears it).</summary>
        public static event Action<int, EntityMarker> OnMarkerChanged;

        public static EntityMarker Get(int entityId) => _markers.TryGetValue(entityId, out var marker) ? marker : EntityMarker.None;

        /// <summary>Back to no markers (called when returning to character select).</summary>
        public static void ResetSession()
        {
            _markers.Clear();
        }

        public static void HandleEntityMarker(Packet packet)
        {
            int entityId = packet.ReadInt();
            var marker = (EntityMarker)packet.ReadByte();

            if (marker == EntityMarker.None) _markers.Remove(entityId);
            else _markers[entityId] = marker;

            OnMarkerChanged?.Invoke(entityId, marker);
        }
    }
}
