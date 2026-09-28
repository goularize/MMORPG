using System;
using System.Diagnostics;
using System.Threading;

namespace Server.World
{
    public class GameLogic
    {
        // Target Ticks Per Second
        private const int TICKS_PER_SECOND = 30;
        
        // How many milliseconds each tick should take to maintain 30 TPS (approx 33.33ms)
        private const float MS_PER_TICK = 1000f / TICKS_PER_SECOND;

        private bool _isRunning = false;
        private Thread _logicThread;

        public void Start()
        {
            _isRunning = true;
            _logicThread = new Thread(Loop)
            {
                Name = "GameLogicThread",
                IsBackground = true
            };
            _logicThread.Start();
            
            Console.WriteLine($"GameLogic started at {TICKS_PER_SECOND} TPS (Target: {MS_PER_TICK:F2}ms per tick).");
        }

        public void Stop()
        {
            _isRunning = false;
            // Wait for the thread to finish its current loop before killing it completely
            _logicThread?.Join(1000); 
            Console.WriteLine("GameLogic stopped.");
        }

        private void Loop()
        {
            Stopwatch timer = new Stopwatch();

            while (_isRunning)
            {
                timer.Restart();

                // 1. Process all game logic for this tick
                Update();

                timer.Stop();
                
                // 2. Calculate how long the logic took to execute
                long elapsedMs = timer.ElapsedMilliseconds;

                // 3. Sleep for the remaining time to ensure a steady tick rate
                if (elapsedMs < MS_PER_TICK)
                {
                    int sleepTime = (int)(MS_PER_TICK - elapsedMs);
                    Thread.Sleep(sleepTime);
                }
                else
                {
                    // If elapsedMs > MS_PER_TICK, the server is struggling to keep up!
                    // In a production environment, you might log a warning here (Server Lag).
                }
            }
        }

        /// <summary>
        /// The main update method where all world state calculations happen.
        /// (e.g. Movement, Physics, Combat, AI)
        /// </summary>
        private void Update()
        {
            // Placeholder: No game mechanics implemented yet.
        }
    }
}
