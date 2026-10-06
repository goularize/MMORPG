using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    /// <summary>Network I/O hardening of <see cref="ClientConnection"/>, exercised over real loopback sockets.</summary>
    public class ClientConnectionIoTests : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly TcpClient _peer = new();
        private readonly TcpClient _serverSide;
        private readonly ManualResetEventSlim _disconnected = new(false);

        public ClientConnectionIoTests()
        {
            Environment.SetEnvironmentVariable("PRE_AUTH_IDLE_TIMEOUT_SECONDS", "0.3");
            Environment.SetEnvironmentVariable("PACKET_COMPLETION_TIMEOUT_SECONDS", "0.3");
            ServerConfig.Initialize();

            _listener.Start();
            var accept = _listener.AcceptTcpClientAsync();
            _peer.Connect(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndpoint).Port);
            _serverSide = accept.Result;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PRE_AUTH_IDLE_TIMEOUT_SECONDS", null);
            Environment.SetEnvironmentVariable("PACKET_COMPLETION_TIMEOUT_SECONDS", null);
            ServerConfig.Initialize();
            _peer.Dispose();
            _serverSide.Dispose();
            _listener.Stop();
        }

        private ClientConnection Start()
        {
            var connection = new ClientConnection(1, _serverSide, _ => _disconnected.Set());
            connection.StartHandling();
            return connection;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(ushort.MaxValue)] // above the inbound cap
        public void InvalidFrameLength_DisconnectsClient(ushort length)
        {
            Start();
            _peer.GetStream().Write(BitConverter.GetBytes(length));

            Assert.True(_disconnected.Wait(TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void IncompletePacket_TimesOut()
        {
            Start();
            // Header promises 100 bytes, only 3 arrive
            _peer.GetStream().Write(new byte[] { 100, 0, 1 });

            Assert.True(_disconnected.Wait(TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void SilentUnauthenticatedConnection_TimesOut()
        {
            Start();

            Assert.True(_disconnected.Wait(TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void AuthenticatedIdleConnection_IsNotTimedOut()
        {
            // Signed in before the first read starts, as it is for a connection past its sign-in packet
            var connection = new ClientConnection(1, _serverSide, _ => _disconnected.Set()) { AccountId = 1 };
            connection.StartHandling();

            Assert.False(_disconnected.Wait(TimeSpan.FromMilliseconds(800)));
        }

        [Fact]
        public async Task ConcurrentSends_DoNotInterleaveFrames()
        {
            var connection = new ClientConnection(1, _serverSide, _ => _disconnected.Set()) { AccountId = 1 };
            connection.StartHandling();
            const int threads = 8, perThread = 200;

            var senders = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                senders[t] = Task.Run(() =>
                {
                    for (int i = 0; i < perThread; i++)
                    {
                        using var p = new Packet(OpCode.SignInRequest);
                        p.Write(new string('x', 500));
                        connection.Send(p);
                    }
                });
            }

            // Every frame must parse cleanly: length header, valid opcode, intact payload
            var stream = _peer.GetStream();
            stream.ReadTimeout = 5000;
            var header = new byte[Packet.HeaderSize];
            for (int n = 0; n < threads * perThread; n++)
            {
                ReadExactly(stream, header);
                int length = BitConverter.ToUInt16(header, 0);
                Assert.Equal((ushort)OpCode.SignInRequest, BitConverter.ToUInt16(header, 2));
                var payload = new byte[length - Packet.HeaderSize];
                ReadExactly(stream, payload);
                Assert.Equal(new string('x', 500), new Packet(Concat(header, payload)).ReadString());
            }

            await Task.WhenAll(senders);
        }

        [Fact]
        public void OversizedOutboundPacket_Throws()
        {
            using var p = new Packet(OpCode.SignInRequest);
            p.Write(new string('x', ushort.MaxValue));

            Assert.Throws<InvalidOperationException>(() => p.ToArray());
        }

        private static void ReadExactly(NetworkStream s, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = s.Read(buffer, read, buffer.Length - read);
                Assert.True(n > 0, "connection closed early");
                read += n;
            }
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            return r;
        }
    }
}
