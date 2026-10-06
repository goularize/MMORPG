using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Server.Network;
using Shared.Network;

namespace Server
{
    public class ClientConnection : IClientConnection
    {
        public int Id { get; }
        public string RemoteAddress { get; }

        // Written by one thread and read by others (read loop vs game thread). A Nullable<int> is two fields and
        // can tear, so each value is stored as a single int where 0 means "none" (IDs start at 1).
        private int _accountId;
        private int _playerId;

        public int? AccountId // Set when the player successfully signs in
        {
            get { int v = Volatile.Read(ref _accountId); return v == 0 ? null : v; }
            set => Volatile.Write(ref _accountId, value ?? 0);
        }

        public int? PlayerId // Set when the player selects a character (cleared if entering the world fails)
        {
            get { int v = Volatile.Read(ref _playerId); return v == 0 ? null : v; }
            set => Volatile.Write(ref _playerId, value ?? 0);
        }
        
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;
        private readonly byte[] _receiveBuffer;
        private readonly Action<int> _onDisconnect;
        private int _closed;
        private readonly object _sendLock = new();

        // Packet framing variables
        private byte[]? _packetBytes;

        public ClientConnection(int id, TcpClient tcpClient, Action<int> onDisconnect)
        {
            Id = id;
            _tcpClient = tcpClient;
            // Captured now: the socket's endpoint is unavailable once the connection is closed
            RemoteAddress = (tcpClient.Client.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "unknown";
            _stream = tcpClient.GetStream();
            _receiveBuffer = new byte[4096]; // 4KB buffer for incoming data
            // A client that stops reading must not stall the thread holding _sendLock (often the game loop) forever
            _stream.WriteTimeout = (int)(ServerConfig.SendTimeoutSeconds * 1000);
            _onDisconnect = onDisconnect;
        }

        public bool IsConnected => Volatile.Read(ref _closed) == 0;

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
                    // Read incoming bytes. A half-received packet must complete quickly (slow-drip attacks) and a
                    // connection that has not signed in yet must not idle forever holding a socket.
                    bool midPacket = _packetBytes is { Length: > 0 };
                    double timeoutSeconds = midPacket ? ServerConfig.PacketCompletionTimeoutSeconds
                        : AccountId == null ? ServerConfig.PreAuthIdleTimeoutSeconds
                        : 0;

                    using var readTimeout = new CancellationTokenSource();
                    if (timeoutSeconds > 0) readTimeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                    int bytesRead;
                    try
                    {
                        bytesRead = await _stream.ReadAsync(_receiveBuffer, 0, _receiveBuffer.Length, readTimeout.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine($"Client {Id} timed out ({(midPacket ? "incomplete packet" : "no sign-in")}). Disconnecting.");
                        Disconnect();
                        break;
                    }

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

                // Security check: a frame must at least hold its own header (length + opcode) and must not exceed
                // the inbound cap, otherwise we would buffer attacker-chosen amounts of data waiting for it.
                if (expectedLength < Packet.HeaderSize || expectedLength > ServerConfig.MaxInboundPacketBytes)
                {
                    Console.WriteLine($"Client {Id} sent a packet with invalid length {expectedLength}. Disconnecting.");
                    Disconnect();
                    return;
                }

                if (_packetBytes.Length >= expectedLength)
                {
                    // We have a complete packet! Extract it.
                    byte[] completePacket = new byte[expectedLength];
                    Array.Copy(_packetBytes, 0, completePacket, 0, expectedLength);

                    // Session packets run right here; world packets are queued for the game thread
                    PacketHandler.Receive(this, completePacket);
                    if (!IsConnected) return; // e.g. dropped for flooding the command queue

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
                byte[] data = packet.ToArray();
                // Game-loop and network threads both send; unsynchronized writes could interleave frames
                lock (_sendLock)
                {
                    if (IsConnected && _tcpClient.Connected)
                    {
                        _stream.Write(data, 0, data.Length);
                    }
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
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;

            _stream.Close();
            _tcpClient.Close();
            _onDisconnect?.Invoke(Id);
        }
    }
}
