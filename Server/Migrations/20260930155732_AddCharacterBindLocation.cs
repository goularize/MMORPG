using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterBindLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BindMapId",
                table: "Characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "BindX",
                table: "Characters",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BindY",
                table: "Characters",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BindZ",
                table: "Characters",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BindMapId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "BindX",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "BindY",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "BindZ",
                table: "Characters");
        }
    }
}
