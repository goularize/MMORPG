# World Server Architecture

The World Server is the authoritative engine driving the gameplay. While the `GameServer` (Networking Layer) is responsible for constantly listening to clients and receiving bytes as fast as possible, the **World Server** processes those actions at a strictly controlled pace.

## The Game Loop & Tick Rate

Located in `Server/World/GameLogic.cs`, the server runs a dedicated background thread specifically for calculating game state.

### Why a Fixed Tick Rate?
If we processed player movement the exact millisecond a packet arrived, players with faster internet connections could move faster than players with slow internet, or they could spam the server with packets to crash it. 

To solve this, we use a **Fixed Tick Rate** (currently set to **30 Ticks Per Second**).

### How It Works
1. **Target Time:** At 30 TPS, each "tick" of the server must take exactly `33.33 milliseconds`.
2. **Execution:** The `GameLogic.Update()` method runs, processing all queued player movements, combat damage, and AI behaviors for that split second.
3. **Compensation:** The server checks a `Stopwatch` to see how long the `Update()` method took to execute. 
   - If it took `5ms`, the thread goes to `Sleep` for the remaining `28.33ms`.
   - By perfectly timing these sleeps, the server maintains an identical heartbeat regardless of how fast the CPU is.

### Engine Overload (Lag)
If the `Update()` method ever takes *longer* than `33.33ms` to execute, it means the CPU is maxed out and struggling to handle the math (usually due to too many players in one area). When this happens, the thread does not sleep at all and immediately starts the next tick to catch up. This is what players experience as "Server Lag".
