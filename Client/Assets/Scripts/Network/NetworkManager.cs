using System;
using System.Net.Sockets;
using UnityEngine;

namespace Client.Network
{
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }

        [Header("Server Connection")]
        public string serverIP = "127.0.0.1";
        public int serverPort = 7777;

        public const string GameVersion = Shared.Constants.GameRules.GameVersion;

        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private byte[] _receiveBuffer;
        private byte[] _packetBytes;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject); // Keep the network alive across scenes!
                Application.runInBackground = true; // <--- Keep processing packets when window loses focus!
                PacketHandler.Initialize(); // Register all network routes
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            ConnectToServer();
        }

        private void ConnectToServer()
        {
            try
            {
                _tcpClient = new TcpClient();
                _receiveBuffer = new byte[4096];
                
                // Asynchronous connection so the Unity UI doesn't freeze
                _tcpClient.BeginConnect(serverIP, serverPort, OnConnect, null);
                Debug.Log($"[Network] Attempting to connect to {serverIP}:{serverPort}...");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Network] Connection initialization failed: {ex.Message}");
            }
        }

        private void OnConnect(IAsyncResult result)
        {
            try
            {
                _tcpClient.EndConnect(result);
                
                if (!_tcpClient.Connected) return;

                _stream = _tcpClient.GetStream();
                Debug.Log("[Network] Successfully connected to the server!");

                // Start listening for incoming packets
                _stream.BeginRead(_receiveBuffer, 0, _receiveBuffer.Length, OnReceive, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Network] Connection failed: {ex.Message}");
            }
        }

        private void OnReceive(IAsyncResult result)
        {
            try
            {
                if (_stream == null) return;

                int bytesRead = _stream.EndRead(result);
                if (bytesRead <= 0)
                {
                    Disconnect();
                    return;
                }

                // Append incoming bytes to our packet buffer
                ProcessIncomingBytes(_receiveBuffer, bytesRead);

                // Continue listening
                _stream.BeginRead(_receiveBuffer, 0, _receiveBuffer.Length, OnReceive, null);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Disconnect();
            }
        }

        private System.Collections.Concurrent.ConcurrentQueue<byte[]> _packetQueue = new();

        private void Update()
        {
            // Process all packets that arrived on the background thread
            while (_packetQueue.TryDequeue(out byte[] packetData))
            {
                // Safely handle the packet on the Unity Main Thread
                PacketHandler.HandlePacket(packetData);
            }
        }

        private void ProcessIncomingBytes(byte[] buffer, int length)
        {
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

            while (_packetBytes != null && _packetBytes.Length >= 2)
            {
                ushort expectedLength = BitConverter.ToUInt16(_packetBytes, 0);

                if (expectedLength == 0)
                {
                    Debug.LogWarning("[Network] Received corrupt packet (length 0). Disconnecting.");
                    Disconnect();
                    return;
                }

                if (_packetBytes.Length >= expectedLength)
                {
                    byte[] completePacket = new byte[expectedLength];
                    Array.Copy(_packetBytes, 0, completePacket, 0, expectedLength);

                    // Safely queue the packet to be handled by the Unity Main Thread in Update()
                    _packetQueue.Enqueue(completePacket);

                    int remainingBytes = _packetBytes.Length - expectedLength;
                    if (remainingBytes > 0)
                    {
                        byte[] newBuffer = new byte[remainingBytes];
                        Array.Copy(_packetBytes, expectedLength, newBuffer, 0, remainingBytes);
                        _packetBytes = newBuffer;
                    }
                    else
                    {
                        _packetBytes = null;
                    }
                }
                else
                {
                    break; // Wait for more data
                }
            }
        }

        public void SendPacket(Shared.Network.Packet packet)
        {
            try
            {
                if (_tcpClient != null && _tcpClient.Connected)
                {
                    byte[] data = packet.ToArray();
                    _stream.BeginWrite(data, 0, data.Length, null, null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Network] Error sending packet: {ex.Message}");
                Disconnect();
            }
        }

        public void Disconnect()
        {
            Debug.Log("[Network] Disconnected from server.");
            _stream?.Close();
            _tcpClient?.Close();
        }

        private void OnApplicationQuit()
        {
            Disconnect();
        }
    }
}
