using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_account",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    salt = table.Column<byte[]>(type: "bytea", nullable: false),
                    storage_mode = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_local_account", x => x.id);
                    table.ForeignKey(
                        name: "fk_local_account_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recovery_code",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    local_account_id = table.Column<long>(type: "bigint", nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recovery_code", x => x.id);
                    table.ForeignKey(
                        name: "fk_recovery_code_local_account_local_account_id",
                        column: x => x.local_account_id,
                        principalSchema: "fortuna",
                        principalTable: "local_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_local_account_name",
                schema: "fortuna",
                table: "local_account",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_account_public_id",
                schema: "fortuna",
                table: "local_account",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_account_user_id",
                schema: "fortuna",
                table: "local_account",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recovery_code_local_account_id_code_hash",
                schema: "fortuna",
                table: "recovery_code",
                columns: new[] { "local_account_id", "code_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recovery_code",
                schema: "fortuna");

            migrationBuilder.DropTable(
                name: "local_account",
                schema: "fortuna");
        }
    }
}
