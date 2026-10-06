using System.Threading.Tasks;
using Xunit;

namespace Server.Tests
{
    public class ShutdownCoordinatorTests
    {
        [Fact]
        public void Request_CancelsTokenAndRecordsReason()
        {
            using var shutdown = new ShutdownCoordinator();
            Assert.False(shutdown.IsRequested);

            Assert.True(shutdown.Request("SIGTERM"));

            Assert.True(shutdown.IsRequested);
            Assert.True(shutdown.Token.IsCancellationRequested);
            Assert.Equal("SIGTERM", shutdown.Reason);
        }

        [Fact]
        public void Request_IsIdempotentAndKeepsTheFirstReason()
        {
            using var shutdown = new ShutdownCoordinator();

            Assert.True(shutdown.Request("SIGTERM"));
            Assert.False(shutdown.Request("Ctrl+C"));

            Assert.Equal("SIGTERM", shutdown.Reason);
        }

        [Fact]
        public async Task Request_FromAnotherThread_ReleasesAWaitingMainThread()
        {
            using var shutdown = new ShutdownCoordinator();
            var waiter = Task.Run(() => shutdown.Token.WaitHandle.WaitOne(5000));

            await Task.Run(() => shutdown.Request("test"));

            Assert.True(await waiter);
        }

        [Fact]
        public void Request_RacingCallers_OnlyOneWins()
        {
            using var shutdown = new ShutdownCoordinator();
            int winners = 0;

            Parallel.For(0, 32, i => { if (shutdown.Request($"r{i}")) System.Threading.Interlocked.Increment(ref winners); });

            Assert.Equal(1, winners);
        }
    }
}
