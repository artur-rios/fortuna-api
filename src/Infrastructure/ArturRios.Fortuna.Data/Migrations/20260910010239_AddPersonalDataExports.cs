using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalDataExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_data_export_format",
                schema: "fortuna",
                table: "data_export");

            migrationBuilder.AddColumn<short>(
                name: "kind",
                schema: "fortuna",
                table: "data_export",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_data_export_format",
                schema: "fortuna",
                table: "data_export",
                sql: "format BETWEEN 1 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_data_export_kind",
                schema: "fortuna",
                table: "data_export",
                sql: "kind BETWEEN 1 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "ck_data_export_kind_format",
                schema: "fortuna",
                table: "data_export",
                sql: "(kind = 1 AND format BETWEEN 1 AND 3) OR (kind = 2 AND format = 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_data_export_format",
                schema: "fortuna",
                table: "data_export");

            migrationBuilder.DropCheckConstraint(
                name: "ck_data_export_kind",
                schema: "fortuna",
                table: "data_export");

            migrationBuilder.DropCheckConstraint(
                name: "ck_data_export_kind_format",
                schema: "fortuna",
                table: "data_export");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "fortuna",
                table: "data_export");

            migrationBuilder.AddCheckConstraint(
                name: "ck_data_export_format",
                schema: "fortuna",
                table: "data_export",
                sql: "format BETWEEN 1 AND 3");
        }
    }
}
