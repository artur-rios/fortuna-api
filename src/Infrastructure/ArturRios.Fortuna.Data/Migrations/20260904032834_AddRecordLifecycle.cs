using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_audit_entry_user_profiles_user_id",
                schema: "fortuna",
                table: "audit_entry");

            migrationBuilder.Sql("DROP TRIGGER audit_entry_no_update ON fortuna.audit_entry;");

            migrationBuilder.AddColumn<Guid>(
                name: "deletion_cascade_id",
                schema: "fortuna",
                table: "user",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "actor_user_id",
                schema: "fortuna",
                table: "audit_entry",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE fortuna.audit_entry AS audit
                SET actor_user_id = actor.public_id
                FROM fortuna."user" AS actor
                WHERE audit.user_id = actor.id;
                """);

            migrationBuilder.Sql(
                """
                UPDATE fortuna."user"
                SET deletion_cascade_id = gen_random_uuid()
                WHERE is_deleted AND deletion_cascade_id IS NULL;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_entry_no_update
                BEFORE UPDATE ON fortuna.audit_entry
                FOR EACH STATEMENT EXECUTE FUNCTION fortuna.audit_entry_is_append_only();
                """);

            migrationBuilder.DropIndex(
                name: "ix_audit_entry_user_id",
                schema: "fortuna",
                table: "audit_entry");

            migrationBuilder.DropColumn(
                name: "user_id",
                schema: "fortuna",
                table: "audit_entry");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_deletion_state",
                schema: "fortuna",
                table: "user",
                sql: "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_actor_user_id",
                schema: "fortuna",
                table: "audit_entry",
                column: "actor_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_deletion_state",
                schema: "fortuna",
                table: "user");

            migrationBuilder.DropIndex(
                name: "ix_audit_entry_actor_user_id",
                schema: "fortuna",
                table: "audit_entry");

            migrationBuilder.Sql("DROP TRIGGER audit_entry_no_update ON fortuna.audit_entry;");

            migrationBuilder.AddColumn<long>(
                name: "user_id",
                schema: "fortuna",
                table: "audit_entry",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE fortuna.audit_entry AS audit
                SET user_id = actor.id
                FROM fortuna."user" AS actor
                WHERE audit.actor_user_id = actor.public_id;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_entry_no_update
                BEFORE UPDATE ON fortuna.audit_entry
                FOR EACH STATEMENT EXECUTE FUNCTION fortuna.audit_entry_is_append_only();
                """);

            migrationBuilder.DropColumn(
                name: "deletion_cascade_id",
                schema: "fortuna",
                table: "user");

            migrationBuilder.DropColumn(
                name: "actor_user_id",
                schema: "fortuna",
                table: "audit_entry");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_user_id",
                schema: "fortuna",
                table: "audit_entry",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_audit_entry_user_profiles_user_id",
                schema: "fortuna",
                table: "audit_entry",
                column: "user_id",
                principalSchema: "fortuna",
                principalTable: "user",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
