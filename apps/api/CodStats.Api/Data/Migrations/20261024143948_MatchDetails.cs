using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodStats.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MatchDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "eliminations",
                table: "matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "enemy_score",
                table: "matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "level",
                table: "matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mode_stats",
                table: "matches",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "matches",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "completed");

            migrationBuilder.AddColumn<int>(
                name: "team_score",
                table: "matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "xp",
                table: "matches",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "eliminations",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "enemy_score",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "level",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "mode_stats",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "status",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "team_score",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "xp",
                table: "matches");
        }
    }
}
