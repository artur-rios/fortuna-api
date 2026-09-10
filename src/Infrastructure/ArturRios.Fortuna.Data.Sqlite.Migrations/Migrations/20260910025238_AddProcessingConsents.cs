using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArturRios.Fortuna.Data.Sqlite.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingConsents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processing_consent",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    purpose = table.Column<short>(type: "INTEGER", nullable: false),
                    version = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    granted_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_consent", x => x.id);
                    table.CheckConstraint("ck_processing_consent_purpose", "purpose = 1");
                    table.ForeignKey(
                        name: "fk_processing_consent_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_processing_consent_public_id",
                table: "processing_consent",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_processing_consent_user_id_purpose",
                table: "processing_consent",
                columns: new[] { "user_id", "purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processing_consent");
        }
    }
}
