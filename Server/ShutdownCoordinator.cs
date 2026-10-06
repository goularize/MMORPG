using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Server
{
    /// <summary>
    /// Turns every way of asking the process to stop (SIGTERM from Docker/systemd, Ctrl+C, 'q' on an interactive
    /// console) into one cancellation token, so <c>Program.Main</c> can wait on it and then run the shutdown
    /// path (stop loop, disconnect, flush saves) instead of the process being killed mid-write.
    /// </summary>
    public sealed class ShutdownCoordinator : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private PosixSignalRegistration? _sigterm;
        private PosixSignalRegistration? _sigint;
        private PosixSignalRegistration? _sighup;
        private ConsoleCancelEventHandler? _cancelHandler;
        private int _requests;

        public CancellationToken Token => _cts.Token;
        public bool IsRequested => _cts.IsCancellationRequested;

        /// <summary>Why shutdown was requested first (null until then).</summary>
        public string? Reason { get; private set; }

        /// <summary>Asks the server to stop. Safe to call repeatedly and from any thread; only the first call counts.</summary>
        public bool Request(string reason)
        {
            if (Interlocked.Increment(ref _requests) != 1) return false;

            Reason = reason;
            Console.WriteLine($"[Shutdown] Requested ({reason}). Stopping...");
            _cts.Cancel();
            return true;
        }

        /// <summary>Hooks SIGTERM, SIGINT/Ctrl+C and SIGHUP. A repeated Ctrl+C while stopping force-quits.</summary>
        public void InstallSignalHandlers()
        {
            _sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => OnSignal(ctx, "SIGTERM"));
            _sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => OnSignal(ctx, "SIGINT"));
            _sighup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, ctx => OnSignal(ctx, "SIGHUP"));

            // Windows consoles deliver Ctrl+C/Break here; on Linux it is already covered by SIGINT above.
            _cancelHandler = (_, e) =>
            {
                // A second Ctrl+C while the first shutdown is still running is the operator giving up: let the runtime kill us.
                if (!IsRequested) e.Cancel = true;
                Request("Ctrl+C");
            };
            Console.CancelKeyPress += _cancelHandler;
        }

        private void OnSignal(PosixSignalContext context, string name)
        {
            // Cancel the default action (immediate termination) only for the first request; a repeat falls through.
            context.Cancel = !IsRequested;
            Request(name);
        }

        /// <summary>
        /// Lets an operator type 'q' to stop, but only when a real console is attached: with redirected stdin
        /// (Docker without -it, systemd, CI) there is nothing to read and ReadKey would throw.
        /// </summary>
        public bool StartConsoleListener()
        {
            if (Console.IsInputRedirected) return false;

            var thread = new Thread(() =>
            {
                try
                {
                    while (!IsRequested)
                    {
                        if (Console.ReadKey(intercept: true).KeyChar == 'q')
                        {
                            Request("'q' pressed");
                            return;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // Console went away; signals remain the way to stop.
                }
            })
            {
                Name = "ConsoleListener",
                IsBackground = true
            };
            thread.Start();
            return true;
        }

        public void Dispose()
        {
            _sigterm?.Dispose();
            _sigint?.Dispose();
            _sighup?.Dispose();
            if (_cancelHandler != null) Console.CancelKeyPress -= _cancelHandler;
            _cts.Dispose();
        }
    }
}
