using Shared.Network;

namespace Server.Network
{
    public interface IClientConnection
    {
        int Id { get; }
        int? AccountId { get; set; }
        int? PlayerId { get; set; }
        /// <summary>False once the connection has been closed; queued work for a closed client must be skipped.</summary>
        bool IsConnected { get; }
        /// <summary>Remote IP, used to throttle abuse per address. Connections without one share a single bucket.</summary>
        string RemoteAddress => "unknown";
        void Send(Packet packet);
        void Disconnect();
    }
}
