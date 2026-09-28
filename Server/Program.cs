using System;
using Server.Network;

namespace Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "MMORPG Server";
            
            // Initialize network routing
            PacketHandler.Initialize();
            
            GameServer server = new GameServer(7777);
            server.Start();

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

            server.Stop();
        }
    }
}
