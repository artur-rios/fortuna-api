using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class MaterializeRecurringOccurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_possible_duplicate",
                schema: "fortuna",
                table: "financial_transaction",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "recurring_transaction_id",
                schema: "fortuna",
                table: "financial_transaction",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_recurring_transaction_id_occurred_on",
                schema: "fortuna",
                table: "financial_transaction",
                columns: new[] { "recurring_transaction_id", "occurred_on" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_financial_transaction_recurring_transactions_recurring_tran",
                schema: "fortuna",
                table: "financial_transaction",
                column: "recurring_transaction_id",
                principalSchema: "fortuna",
                principalTable: "recurring_transaction",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_financial_transaction_recurring_transactions_recurring_tran",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropIndex(
                name: "ix_financial_transaction_recurring_transaction_id_occurred_on",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "is_possible_duplicate",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "recurring_transaction_id",
                schema: "fortuna",
                table: "financial_transaction");
        }
    }
}
