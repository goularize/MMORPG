using System;
using Server.Network;
using Server.World;

namespace Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "MMORPG Server";
            
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
