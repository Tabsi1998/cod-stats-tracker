using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CodStats.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    name_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    recovery_key_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "used_nonces",
                columns: table => new
                {
                    signer = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_used_nonces", x => new { x.signer, x.nonce });
                });

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    client_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_devices_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    mode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kills = table.Column<int>(type: "integer", nullable: true),
                    deaths = table.Column<int>(type: "integer", nullable: true),
                    assists = table.Column<int>(type: "integer", nullable: true),
                    result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    placement = table.Column<int>(type: "integer", nullable: true),
                    map = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    score = table.Column<int>(type: "integer", nullable: true),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    review_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    recognizer_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_matches", x => x.id);
                    table.ForeignKey(
                        name: "fk_matches_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "name_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_name_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_name_history_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_devices_profile_id",
                table: "devices",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_devices_public_key",
                table: "devices",
                column: "public_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_matches_game_profile_id",
                table: "matches",
                columns: new[] { "game", "profile_id" });

            migrationBuilder.CreateIndex(
                name: "ix_matches_profile_id_played_at",
                table: "matches",
                columns: new[] { "profile_id", "played_at" });

            migrationBuilder.CreateIndex(
                name: "ix_name_history_profile_id_name",
                table: "name_history",
                columns: new[] { "profile_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_profiles_display_name",
                table: "profiles",
                column: "display_name");

            migrationBuilder.CreateIndex(
                name: "ix_profiles_recovery_key_hash",
                table: "profiles",
                column: "recovery_key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_used_nonces_expires_at",
                table: "used_nonces",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropTable(
                name: "matches");

            migrationBuilder.DropTable(
                name: "name_history");

            migrationBuilder.DropTable(
                name: "used_nonces");

            migrationBuilder.DropTable(
                name: "profiles");
        }
    }
}
