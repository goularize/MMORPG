namespace Shared.Enums
{
    public enum MobBehaviorType
    {
        Friendly = 0,   // Cannot be attacked, does not initiate combat (e.g. Town Blacksmith, merchants)
        Passive = 1,    // Neutral. Never initiates combat. If attacked, retaliates against the attacker.
        Aggressive = 2, // Hostile. Attacks players on sight within AggroRadius.
        Pack = 3        // Social. Attacks when a nearby pack member is attacked or enters combat.
    }
}
