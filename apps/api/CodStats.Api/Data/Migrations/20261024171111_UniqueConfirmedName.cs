using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodStats.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueConfirmedName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Names that are already confirmed twice (exactly as written; case matters in Call of Duty): the older profile keeps the name, the newer becomes
            // uncertain (hidden) until its owner takes the older profile over or merges them.
            migrationBuilder.Sql("""
                UPDATE profiles p SET name_status = 'uncertain'
                WHERE p.name_status = 'confirmed' AND EXISTS (
                    SELECT 1 FROM profiles o
                    WHERE o.name_status = 'confirmed' AND o.display_name = p.display_name
                      AND (o.created_at < p.created_at OR (o.created_at = p.created_at AND o.id < p.id)));
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX ix_profiles_confirmed_name ON profiles (display_name) WHERE name_status = 'confirmed';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_profiles_confirmed_name;");
        }
    }
}
