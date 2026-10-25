using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodStats.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LobbyComparison : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "lobby_rank",
                table: "matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "lobby_size",
                table: "matches",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lobby_rank",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "lobby_size",
                table: "matches");
        }
    }
}
