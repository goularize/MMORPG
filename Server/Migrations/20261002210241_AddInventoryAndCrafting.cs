using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAndCrafting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Gold",
                table: "Characters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "InventorySlots",
                table: "Characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CharacterItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacterId = table.Column<int>(type: "integer", nullable: false),
                    TemplateId = table.Column<int>(type: "integer", nullable: false),
                    BagIndex = table.Column<int>(type: "integer", nullable: false),
                    SlotIndex = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    IsEquipped = table.Column<bool>(type: "boolean", nullable: false),
                    EquippedSlot = table.Column<byte>(type: "smallint", nullable: true),
                    UpgradeLevel = table.Column<int>(type: "integer", nullable: false),
                    Rarity = table.Column<byte>(type: "smallint", nullable: false),
                    RolledPhysicalAttack = table.Column<int>(type: "integer", nullable: false),
                    RolledMagicAttack = table.Column<int>(type: "integer", nullable: false),
                    RolledPhysicalDefense = table.Column<int>(type: "integer", nullable: false),
                    RolledMagicDefense = table.Column<int>(type: "integer", nullable: false),
                    RolledStrength = table.Column<int>(type: "integer", nullable: false),
                    RolledIntelligence = table.Column<int>(type: "integer", nullable: false),
                    RolledConstitution = table.Column<int>(type: "integer", nullable: false),
                    RolledKnowledge = table.Column<int>(type: "integer", nullable: false),
                    RolledCritChance = table.Column<float>(type: "real", nullable: false),
                    RolledCritMultiplier = table.Column<float>(type: "real", nullable: false),
                    RolledDodgeChance = table.Column<float>(type: "real", nullable: false),
                    RolledMovementSpeed = table.Column<float>(type: "real", nullable: false),
                    RolledAttackSpeedBonus = table.Column<float>(type: "real", nullable: false),
                    RolledHealthRegen = table.Column<int>(type: "integer", nullable: false),
                    RolledManaRegen = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterItems_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterLearnedRecipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<int>(type: "integer", nullable: false),
                    RecipeId = table.Column<int>(type: "integer", nullable: false),
                    LearnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterLearnedRecipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterLearnedRecipes_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterItems_CharacterId_BagIndex_SlotIndex",
                table: "CharacterItems",
                columns: new[] { "CharacterId", "BagIndex", "SlotIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterItems_CharacterId_IsEquipped_EquippedSlot",
                table: "CharacterItems",
                columns: new[] { "CharacterId", "IsEquipped", "EquippedSlot" });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterLearnedRecipes_CharacterId_RecipeId",
                table: "CharacterLearnedRecipes",
                columns: new[] { "CharacterId", "RecipeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterItems");

            migrationBuilder.DropTable(
                name: "CharacterLearnedRecipes");

            migrationBuilder.DropColumn(
                name: "Gold",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "InventorySlots",
                table: "Characters");
        }
    }
}
