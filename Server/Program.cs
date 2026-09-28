using System;
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
            
            // Ensure Database exists (creates mmorpg.db file if using SQLite)
            using (var db = new AppDbContext())
            {
                db.Database.EnsureCreated();
                Console.WriteLine("Database initialized.");
            }

            // Initialize network routing
            PacketHandler.Initialize();
            
            // Start the Networking Layer
            GameServer server = new GameServer(7777);
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
