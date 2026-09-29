using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Server
{
    public class GameServer
    {
        private readonly int _port;
        private readonly TcpListener _listener;
        private bool _isRunning;
        private int _nextClientId = 1;

        // Thread-safe dictionary to keep track of connected players
        private readonly ConcurrentDictionary<int, ClientConnection> _clients = new();

        public GameServer(int port)
        {
            _port = port;
            _listener = new TcpListener(IPAddress.Any, _port);
        }

        public void Start()
        {
            _listener.Start();
            _isRunning = true;
            Console.WriteLine($"Game Server started on port {_port}. Waiting for connections...");

            // Start accepting clients without blocking the main thread
            _ = AcceptClientsAsync();
        }

        private async Task AcceptClientsAsync()
        {
            try
            {
                while (_isRunning)
                {
                    TcpClient tcpClient = await _listener.AcceptTcpClientAsync();

                    int clientId = _nextClientId++;
                    Console.WriteLine($"Incoming connection from {tcpClient.Client.RemoteEndPoint} assigned ID {clientId}.");

                    // Create the client connection object
                    ClientConnection connection = new ClientConnection(clientId, tcpClient, OnClientDisconnect);

                    if (_clients.TryAdd(clientId, connection))
                    {
                        // Start reading data from this client
                        connection.StartHandling();
                    }
                }
            }
            catch (Exception ex)
            {
                // This exception triggers if _listener.Stop() is called while AcceptTcpClientAsync is blocking
                if (_isRunning)
                {
                    Console.WriteLine($"Server error in accept loop: {ex.Message}");
                }
            }
        }

        private void OnClientDisconnect(int clientId)
        {
            if (_clients.TryRemove(clientId, out var clientConnection))
            {
                Console.WriteLine($"Client {clientId} removed from active connections list.");
                
                if (clientConnection.PlayerId.HasValue)
                {
                    Server.World.GameLogic.MapMgr.RemovePlayer(clientConnection.PlayerId.Value);
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener.Stop();

            foreach (var client in _clients.Values)
            {
                client.Disconnect();
            }
            _clients.Clear();
            Console.WriteLine("Server stopped.");
        }
    }
}
