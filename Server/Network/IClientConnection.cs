using Shared.Network;

namespace Server.Network
{
    public interface IClientConnection
    {
        int Id { get; }
        int? AccountId { get; set; }
        int? PlayerId { get; set; }
        void Send(Packet packet);
        void Disconnect();
    }
}
