using Shared.Network;

namespace Server.Network
{
    /// <summary>
    /// Stands in for the connection of a character that is still in the world after its client dropped
    /// (combat-log linger). Everything sent to it is discarded.
    /// </summary>
    public sealed class DetachedConnection : IClientConnection
    {
        public static readonly DetachedConnection Instance = new();

        private DetachedConnection() { }

        public int Id => 0;
        public int? AccountId { get; set; }
        public int? PlayerId { get; set; }
        public bool IsConnected => false;
        public void Send(Packet packet) { }
        public void Disconnect() { }
    }
}
