# Classic MMORPG Features & Roadmap

Drawing inspiration from classic MMORPGs like *World of Warcraft*, *Guild Wars*, *Perfect World*, and *Tibia*, this document serves as a continuous development roadmap. It is structured progressively, ensuring that foundational systems are built before complex, dependent features are introduced.

---

## Phase 1: Core Architecture (✅ Completed)
The absolute minimum required to have a multiplayer environment.
- **Client-Server Architecture:** Persistent TCP socket connections.
- **Binary Protocol:** Custom packet serialization for fast data transfer.
- **Authentication:** Account registration, secure password hashing, and login flow.
- **Authoritative World State:** 30 TPS fixed game loop.
- **Entity System & AoI:** Area of Interest filtering to limit bandwidth usage.

---

## Phase 2: Player Representation & Basic Interaction
Giving the player an identity and allowing basic communication.
- **Character Management:** Character creation (Name, Class/Vocation selection, Appearance), character selection screen, and deletion.
- **World Spawning:** Spawning the character into a starter zone and persisting their X/Y/Z coordinates in the database.
- **Movement Synchronization:** Client prediction and server validation of movement/pathfinding to prevent speed-hacking.
- **Basic Chat System:** Local/Say (radius-based), Global/World chat, and Whispers (Private messages).

---

## Phase 3: The Core Gameplay Loop
The fundamental "Kill Monsters, Get Loot, Level Up" cycle.
- **Stats & Vitals:** Health (HP) and Mana (MP) regeneration, and core attributes (Strength, Agility, Intelligence).
- **Targeting System:** Selecting entities to interact with or attack.
- **Basic Combat:** Auto-attacks, damage calculations (Attack vs. Defense), hit chance, and critical strikes.
- **Mob AI (PvE):** Basic state machine for monsters (Idle, Patrol, Chase, Attack, Flee, Reset).
- **Experience & Leveling:** Gaining EXP from kills, leveling up, and earning stat/skill points.
- **Loot & Basic Inventory:** Dropping items on the ground, picking them up, and a grid or slot-based inventory system.

---

## Phase 4: World Economy & Content
Fleshing out the world and giving players long-term goals.
- **Currency:** A global currency system (e.g., Gold/Silver/Copper or Coins).
- **NPC Vendors:** Interacting with shops to buy potions/gear and sell junk.
- **Equipment System:** Equipping items (Head, Chest, Weapon, Shield, Rings) that visually update the character and modify stats.
- **Quests (Missions):** A system to track objectives (Kill X boars, collect Y items), deliver them to NPCs, and receive rewards.
- **Player Trading:** Secure interface for two players to exchange items and currency.

---

## Phase 5: Social & Cooperative Play
Encouraging players to work together and form communities.
- **Parties (Groups):** Grouping up with a shared UI, EXP sharing, and loot distribution rules (Round Robin, Free-for-All).
- **Friends & Ignore Lists:** Tracking who is online and blocking toxic players.
- **Guilds / Clans:** Persistent groups with a hierarchy (Leader, Officer, Member), a dedicated chat channel, and a guild bank.
- **Buffs & Debuffs (Auras):** Temporary stat modifications, allowing classes like Priests to heal or buff teammates.
- **Skill/Spell System:** A hotbar system for casting abilities with cooldowns, mana costs, and cast times.

---

## Phase 6: Advanced Systems & Endgame
Features that retain players over months and years.
- **Player vs Player (PvP):** Duels, open-world PvP toggles, or dedicated arenas/battlegrounds (e.g., Tibia's skull system or WoW's battlegrounds).
- **Instancing / Dungeons:** Isolated map copies for a specific party to clear a boss.
- **Crafting & Gathering:** Mining, Herbalism, Blacksmithing, etc.
- **Mounts & Fast Travel:** Movement speed boosts and teleportation/flight paths to cross massive worlds quickly.
- **Mail System:** Asynchronous delivery of items and messages between players.
- **Bank / Stash:** Persistent storage for items that don't fit in the inventory.
- **Player Housing:** Customizable persistent spaces for players or guilds.
