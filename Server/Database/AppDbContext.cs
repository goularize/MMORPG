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
        }
    }
}
