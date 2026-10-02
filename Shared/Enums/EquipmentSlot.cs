namespace Shared.Enums
{
    public enum EquipmentSlot : byte
    {
        None = 0,
        Head = 1,       // Visual
        Body = 2,       // Visual
        Feet = 3,       // Visual
        MainHand = 4,   // Visual (1H or 2H Weapon)
        OffHand = 5,    // Visual (Shield / Offhand / Focus)
        Ring = 6,       // Stat only
        Amulet = 7      // Stat only
    }
}
