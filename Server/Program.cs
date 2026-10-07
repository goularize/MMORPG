using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Server.Network;
using Server.World;
using Server.Database;

namespace Server
{
    class Program
    {
        static void Main(string[] args)
        {
            // No console when running under systemd/Docker, so setting the title must not be fatal
            try { Console.Title = "MMORPG Server"; } catch (IOException) { }

            using var shutdown = new ShutdownCoordinator();
            shutdown.InstallSignalHandlers();

            // Load environment variables from .env file
            string? envFile = ServerConfig.FindEnvFile(Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
            if (envFile != null)
            {
                DotNetEnv.Env.Load(envFile);
                Console.WriteLine($"[Config] Loaded {envFile}");
            }
            else
            {
                Console.WriteLine("[Config] No .env file found; using environment variables and defaults.");
            }
            ServerConfig.Initialize();
            Data.DataManager.Initialize();

            // Apply any pending migrations (creates database if it doesn't exist)
            using (var db = new AppDbContext())
            {
                db.Database.Migrate();
                Console.WriteLine("Database and Migrations initialized.");
            }

            // Initialize network routing
            PacketHandler.Initialize();

            // Start the Networking Layer
            GameServer server = new GameServer(ServerConfig.ServerPort);
            server.Start();

            // Start the World Logic Layer
            GameLogic logic = new GameLogic();
            logic.Start();

            // Block until SIGTERM / Ctrl+C / 'q' (the latter only with an interactive console)
            Console.WriteLine(shutdown.StartConsoleListener()
                ? "Press 'q' or Ctrl+C to shut down the server."
                : "Send SIGTERM or Ctrl+C to shut down the server.");
            shutdown.Token.WaitHandle.WaitOne();

            // Graceful shutdown
            logic.Stop();
            server.Stop();

            // The loop is stopped, so queued disconnects will never run: save everyone who is still online, then
            // wait for the write-behind queue to reach the database before the process exits.
            GameLogic.MapMgr.SaveAllPlayers();
            if (!Server.Persistence.PersistenceService.Instance.Stop(TimeSpan.FromSeconds(10)))
            {
                Console.WriteLine("[Persistence] Some pending saves could not be flushed before shutdown.");
            }
        }
    }
}
