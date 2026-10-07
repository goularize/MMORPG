namespace Shared.Enums
{
    /// <summary>What an NPC offers when a player interacts with it (the Action of an Npcs.json interaction).</summary>
    public enum InteractAction : byte
    {
        None = 0,
        BindPoint = 1,
        OpenShop = 2
    }
}
