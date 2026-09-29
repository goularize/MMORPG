using Microsoft.EntityFrameworkCore;
using Server.Database.Models;
using System.IO;

namespace Server.Database
{
    public class AppDbContext : DbContext
    {
        // This DbSet represents the "Accounts" table in the database
        public DbSet<Account> Accounts { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Load connection string from the .env file (or system environment variables)
            string connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") 
                ?? "Host=localhost;Database=mmorpg;Username=postgres;Password=mypassword";
                
            optionsBuilder.UseNpgsql(connectionString);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Enforce that Usernames must be unique
            modelBuilder.Entity<Account>()
                .HasIndex(a => a.Username)
                .IsUnique();
        }
    }
}
