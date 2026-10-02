using Microsoft.EntityFrameworkCore;
using Server.Database.Models;
using System.IO;

namespace Server.Database
{
    public class AppDbContext : DbContext
    {
        // Allows unit tests to override the context creation
        public static Func<AppDbContext> Factory = () => new AppDbContext();

        public AppDbContext() { }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // This DbSet represents the "Accounts" table in the database
        public DbSet<Account> Accounts { get; set; }
        
        // This DbSet represents the "Characters" table
        public DbSet<Character> Characters { get; set; }

        // This DbSet represents the "ChatLogs" table
        public DbSet<ChatLog> ChatLogs { get; set; }

        // This DbSet represents the "CharacterItems" table (inventory & equipment instances)
        public DbSet<CharacterItem> CharacterItems { get; set; }

        // This DbSet represents the "CharacterLearnedRecipes" table
        public DbSet<CharacterLearnedRecipe> CharacterLearnedRecipes { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // Load connection string from the .env file (or system environment variables)
                string connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") 
                    ?? "Host=localhost;Database=mmorpg;Username=postgres;Password=mypassword";
                    
                optionsBuilder.UseNpgsql(connectionString);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Enforce that Usernames must be unique
            modelBuilder.Entity<Account>()
                .HasIndex(a => a.Username)
                .IsUnique();

            // Enforce that Character Names must be unique globally
            modelBuilder.Entity<Character>()
                .HasIndex(c => c.Name)
                .IsUnique();

            // Fast lookup for player inventory slots
            modelBuilder.Entity<CharacterItem>()
                .HasIndex(ci => new { ci.CharacterId, ci.BagIndex, ci.SlotIndex });

            // Fast lookup for equipped items
            modelBuilder.Entity<CharacterItem>()
                .HasIndex(ci => new { ci.CharacterId, ci.IsEquipped, ci.EquippedSlot });

            // Ensure a player cannot learn the same recipe twice
            modelBuilder.Entity<CharacterLearnedRecipe>()
                .HasIndex(clr => new { clr.CharacterId, clr.RecipeId })
                .IsUnique();
        }
    }
}
