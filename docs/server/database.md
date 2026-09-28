# Database Setup

The MMORPG uses **PostgreSQL 16** for persistent storage and **Entity Framework (EF) Core** as the ORM.

## Local Development (Docker)

To run the database locally without polluting your system, we provide a `docker-compose.yml` file.

1. Ensure you have Docker installed.
2. From the root of the project, run:
   ```bash
   docker-compose up -d
   ```
This will start a Postgres 16 instance on port `5432` with a persistent volume (`pgdata`).

## Configuration (`.env`)

For security, the database connection string is never hardcoded. 
You must create a `.env` file in the `Server/` directory.

Copy the provided example:
```bash
cp Server/.env.example Server/.env
```

The default connection string for the local Docker container is:
`DB_CONNECTION_STRING=postgres://postgres:mypassword@localhost/mmorpg`

*Note: The `.env` file is explicitly ignored by `.gitignore` to prevent secret leakage.*

## Entity Framework Core

The `AppDbContext` (`Server/Database/AppDbContext.cs`) bridges the C# code to the PostgreSQL database.

### Running Migrations
Whenever you update or add a new Model in `Server/Database/Models/`, you must create a migration:

```bash
cd Server
dotnet ef migrations add <MigrationName>
```

When the server starts (`Program.cs`), it runs `db.Database.EnsureCreated()`, which automatically builds the tables if they don't exist yet based on your models.

## Security (BCrypt)

**Never store plaintext passwords.**
The `Account` model stores a `PasswordHash`. When a user signs up, the `AuthHandler` uses the `BCrypt.Net-Next` package to cryptographically hash the password before saving it to the database.
