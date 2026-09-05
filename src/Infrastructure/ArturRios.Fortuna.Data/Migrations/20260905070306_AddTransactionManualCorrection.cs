using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionManualCorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_manually_corrected",
                schema: "fortuna",
                table: "financial_transaction",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_manually_corrected",
                schema: "fortuna",
                table: "financial_transaction");
        }
    }
}
