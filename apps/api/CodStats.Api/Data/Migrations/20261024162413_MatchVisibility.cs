using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodStats.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MatchVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_public",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Matches of profiles that are public today stay on the website.
            migrationBuilder.Sql("UPDATE matches SET is_public = TRUE FROM profiles WHERE matches.profile_id = profiles.id AND profiles.is_public;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_public",
                table: "matches");
        }
    }
}
