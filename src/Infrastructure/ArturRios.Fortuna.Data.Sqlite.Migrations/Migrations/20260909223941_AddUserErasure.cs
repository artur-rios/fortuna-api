using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArturRios.Fortuna.Data.Sqlite.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddUserErasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "actor_user_id",
                table: "audit_entry",
                newName: "subject_reference");

            migrationBuilder.RenameIndex(
                name: "ix_audit_entry_actor_user_id",
                table: "audit_entry",
                newName: "ix_audit_entry_subject_reference");

            migrationBuilder.CreateTable(
                name: "audit_subject",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    subject_reference = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_subject", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_subject_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_subject_subject_reference",
                table: "audit_subject",
                column: "subject_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_subject_user_id",
                table: "audit_subject",
                column: "user_id",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO audit_subject (user_id, subject_reference)
                SELECT DISTINCT u.id,
                       lower(hex(randomblob(4))) || '-' ||
                       lower(hex(randomblob(2))) || '-4' ||
                       substr(lower(hex(randomblob(2))), 2) || '-' ||
                       substr('89ab', abs(random()) % 4 + 1, 1) ||
                       substr(lower(hex(randomblob(2))), 2) || '-' ||
                       lower(hex(randomblob(6)))
                FROM user AS u
                JOIN audit_entry AS ae
                  ON ae.subject_reference = u.public_id
                GROUP BY u.id;

                UPDATE audit_entry
                SET subject_reference = (
                    SELECT subject.subject_reference
                    FROM audit_subject AS subject
                    JOIN user AS u ON u.id = subject.user_id
                    WHERE u.public_id = audit_entry.subject_reference)
                WHERE EXISTS (
                    SELECT 1 FROM user AS u
                    WHERE u.public_id = audit_entry.subject_reference);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE audit_entry
                SET subject_reference = (
                    SELECT u.public_id
                    FROM audit_subject AS subject
                    JOIN user AS u ON u.id = subject.user_id
                    WHERE subject.subject_reference = audit_entry.subject_reference)
                WHERE EXISTS (
                    SELECT 1 FROM audit_subject AS subject
                    WHERE subject.subject_reference = audit_entry.subject_reference);
                """);

            migrationBuilder.DropTable(
                name: "audit_subject");

            migrationBuilder.RenameColumn(
                name: "subject_reference",
                table: "audit_entry",
                newName: "actor_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_audit_entry_subject_reference",
                table: "audit_entry",
                newName: "ix_audit_entry_actor_user_id");
        }
    }
}
