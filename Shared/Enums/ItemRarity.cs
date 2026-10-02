namespace Shared.Enums
{
    public enum ItemRarity : byte
    {
        Common = 0,     // Standard rolls, 0 bonus secondary affixes
        Uncommon = 1,   // +10% base roll boost, 1 secondary affix
        Rare = 2,       // +25% base roll boost, 2 secondary affixes
        Epic = 3,       // +40% base roll boost, 3 secondary affixes
        Legendary = 4   // Max base roll boost, 4 secondary affixes
    }
}
