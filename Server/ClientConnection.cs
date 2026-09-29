using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Server.Network;
using Shared.Network;

namespace Server
{
    public class ClientConnection : IClientConnection
    {
        public int Id { get; }
        public int? AccountId { get; set; } // Set when the player successfully signs in
        
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;
        private readonly byte[] _receiveBuffer;
        private readonly Action<int> _onDisconnect;

        // Packet framing variables
        private byte[] _packetBytes;

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

                    // Handle packet framing
                    ProcessIncomingBytes(_receiveBuffer, bytesRead);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Client {Id} connection error: {ex.Message}");
                Disconnect();
            }
        }

        private void ProcessIncomingBytes(byte[] buffer, int length)
        {
            // Append incoming bytes to our persistent packet buffer
            if (_packetBytes == null)
            {
                _packetBytes = new byte[length];
                Array.Copy(buffer, 0, _packetBytes, 0, length);
            }
            else
            {
                byte[] temp = new byte[_packetBytes.Length + length];
                Array.Copy(_packetBytes, 0, temp, 0, _packetBytes.Length);
                Array.Copy(buffer, 0, temp, _packetBytes.Length, length);
                _packetBytes = temp;
            }

            // Loop through the buffer to extract as many complete packets as possible
            while (_packetBytes.Length >= 2) // We need at least 2 bytes to read the packet length
            {
                // Read the expected length of the packet from the first 2 bytes (header)
                ushort expectedLength = BitConverter.ToUInt16(_packetBytes, 0);

                // Security check: if length is 0, someone is sending corrupt data. Prevent infinite loop.
                if (expectedLength == 0)
                {
                    Console.WriteLine($"Client {Id} sent a corrupt packet (length 0). Disconnecting.");
                    Disconnect();
                    return;
                }

                if (_packetBytes.Length >= expectedLength)
                {
                    // We have a complete packet! Extract it.
                    byte[] completePacket = new byte[expectedLength];
                    Array.Copy(_packetBytes, 0, completePacket, 0, expectedLength);

                    // Route it to the PacketHandler (passing 'this' so the handler can reply)
                    PacketHandler.HandlePacket(this, completePacket);

                    // Remove the processed packet from our buffer
                    int remainingBytes = _packetBytes.Length - expectedLength;
                    if (remainingBytes > 0)
                    {
                        byte[] newBuffer = new byte[remainingBytes];
                        Array.Copy(_packetBytes, expectedLength, newBuffer, 0, remainingBytes);
                        _packetBytes = newBuffer;
                    }
                    else
                    {
                        _packetBytes = Array.Empty<byte>(); // Buffer is empty
                    }
                }
                else
                {
                    // We don't have the full packet yet, wait for more data to arrive.
                    break;
                }
            }
        }

        /// <summary>
        /// Finalizes a packet and sends its raw bytes over the network stream to the client.
        /// </summary>
        public void Send(Packet packet)
        {
            try
            {
                if (_tcpClient != null && _tcpClient.Connected)
                {
                    byte[] data = packet.ToArray();
                    // Note: In a production environment, you might want to use _stream.WriteAsync to avoid blocking
                    _stream.Write(data, 0, data.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending data to Client {Id}: {ex.Message}");
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
