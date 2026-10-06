using System;
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
            Console.Title = "MMORPG Server";

            // Load environment variables from .env file
            DotNetEnv.Env.Load();
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

            // Prevent the console app from closing instantly
            Console.WriteLine("Press 'q' to shut down the server.");
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.KeyChar == 'q')
                {
                    break;
                }
            }

            // Graceful shutdown
            logic.Stop();
            server.Stop();
        }
    }
}
