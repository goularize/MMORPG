using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Server
{
    public class ClientConnection
    {
        public int Id { get; }
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;
        private readonly byte[] _receiveBuffer;
        private readonly Action<int> _onDisconnect;

        public ClientConnection(int id, TcpClient tcpClient, Action<int> onDisconnect)
        {
            Id = id;
            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
            _receiveBuffer = new byte[4096]; // 4KB buffer for incoming data
            _onDisconnect = onDisconnect;
        }

        public void StartHandling()
        {
            // Start reading asynchronously without blocking
            _ = ReceiveAsync();
        }

        private async Task ReceiveAsync()
        {
            try
            {
                while (_tcpClient.Connected)
                {
                    // Read incoming bytes
                    int bytesRead = await _stream.ReadAsync(_receiveBuffer, 0, _receiveBuffer.Length);

                    if (bytesRead == 0)
                    {
                        // 0 bytes means the client gracefully closed the connection
                        Console.WriteLine($"Client {Id} disconnected gracefully.");
                        Disconnect();
                        break;
                    }

                    // For now, just log that we received data.
                    // Later, we will pass these bytes to a PacketParser.
                    Console.WriteLine($"Received {bytesRead} bytes from Client {Id}.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Client {Id} connection error: {ex.Message}");
                Disconnect();
            }
        }

        public void Disconnect()
        {
            _stream.Close();
            _tcpClient.Close();
            _onDisconnect?.Invoke(Id);
        }
    }
}
