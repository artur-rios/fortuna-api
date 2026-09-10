using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserErasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "actor_user_id",
                schema: "fortuna",
                table: "audit_entry",
                newName: "subject_reference");

            migrationBuilder.RenameIndex(
                name: "ix_audit_entry_actor_user_id",
                schema: "fortuna",
                table: "audit_entry",
                newName: "ix_audit_entry_subject_reference");

            migrationBuilder.CreateTable(
                name: "audit_subject",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    subject_reference = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_subject", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_subject_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_subject_subject_reference",
                schema: "fortuna",
                table: "audit_subject",
                column: "subject_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_subject_user_id",
                schema: "fortuna",
                table: "audit_subject",
                column: "user_id",
                unique: true);

            migrationBuilder.Sql("DROP TRIGGER audit_entry_no_update ON fortuna.audit_entry;");

            migrationBuilder.Sql("""
                INSERT INTO fortuna.audit_subject (user_id, subject_reference)
                SELECT u.id, gen_random_uuid()
                FROM fortuna."user" AS u
                JOIN fortuna.audit_entry AS ae
                  ON ae.subject_reference = u.public_id
                GROUP BY u.id;

                UPDATE fortuna.audit_entry AS ae
                SET subject_reference = subject.subject_reference
                FROM fortuna.audit_subject AS subject
                JOIN fortuna."user" AS u ON u.id = subject.user_id
                WHERE ae.subject_reference = u.public_id;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER audit_entry_no_update
                BEFORE UPDATE ON fortuna.audit_entry
                FOR EACH STATEMENT EXECUTE FUNCTION fortuna.audit_entry_is_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER audit_entry_no_update ON fortuna.audit_entry;");

            migrationBuilder.Sql("""
                UPDATE fortuna.audit_entry AS ae
                SET subject_reference = u.public_id
                FROM fortuna.audit_subject AS subject
                JOIN fortuna."user" AS u ON u.id = subject.user_id
                WHERE ae.subject_reference = subject.subject_reference;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER audit_entry_no_update
                BEFORE UPDATE ON fortuna.audit_entry
                FOR EACH STATEMENT EXECUTE FUNCTION fortuna.audit_entry_is_append_only();
                """);

            migrationBuilder.DropTable(
                name: "audit_subject",
                schema: "fortuna");

            migrationBuilder.RenameColumn(
                name: "subject_reference",
                schema: "fortuna",
                table: "audit_entry",
                newName: "actor_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_audit_entry_subject_reference",
                schema: "fortuna",
                table: "audit_entry",
                newName: "ix_audit_entry_actor_user_id");
        }
    }
}
