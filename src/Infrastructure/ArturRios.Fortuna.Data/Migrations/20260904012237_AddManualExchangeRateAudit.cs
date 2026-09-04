using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManualExchangeRateAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entry",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    operation = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    entity_public_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome = table.Column<short>(type: "smallint", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entry", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_entry_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_occurred_at",
                schema: "fortuna",
                table: "audit_entry",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_user_id",
                schema: "fortuna",
                table: "audit_entry",
                column: "user_id");

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION fortuna.audit_entry_is_append_only()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION
                        'fortuna.audit_entry is append-only: % is not permitted', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_entry_no_update
                BEFORE UPDATE ON fortuna.audit_entry
                FOR EACH STATEMENT EXECUTE FUNCTION fortuna.audit_entry_is_append_only();
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_entry_no_delete
                BEFORE DELETE ON fortuna.audit_entry
                FOR EACH STATEMENT EXECUTE FUNCTION fortuna.audit_entry_is_append_only();
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_entry_no_truncate
                BEFORE TRUNCATE ON fortuna.audit_entry
                FOR EACH STATEMENT EXECUTE FUNCTION fortuna.audit_entry_is_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_entry_no_truncate ON fortuna.audit_entry;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_entry_no_delete ON fortuna.audit_entry;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_entry_no_update ON fortuna.audit_entry;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fortuna.audit_entry_is_append_only();");

            migrationBuilder.DropTable(
                name: "audit_entry",
                schema: "fortuna");
        }
    }
}
