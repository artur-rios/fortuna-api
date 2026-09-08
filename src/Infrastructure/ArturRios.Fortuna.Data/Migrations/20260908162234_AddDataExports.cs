using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDataExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_export",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    background_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    locale = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    request_json = table.Column<string>(type: "jsonb", nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: true),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_export", x => x.id);
                    table.CheckConstraint("ck_data_export_format", "format BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_data_export_row_count", "row_count IS NULL OR row_count >= 0");
                    table.CheckConstraint("ck_data_export_status", "status BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_data_export_background_job_background_job_id",
                        column: x => x.background_job_id,
                        principalSchema: "fortuna",
                        principalTable: "background_job",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_export_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_export_background_job_id",
                schema: "fortuna",
                table: "data_export",
                column: "background_job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_export_public_id",
                schema: "fortuna",
                table: "data_export",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_export_user_id_created_at",
                schema: "fortuna",
                table: "data_export",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_export",
                schema: "fortuna");
        }
    }
}
