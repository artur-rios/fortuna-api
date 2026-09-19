using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionOccurredOnIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_financial_transaction_user_id_is_deleted",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_user_id_is_deleted_occurred_on",
                schema: "fortuna",
                table: "financial_transaction",
                columns: new[] { "user_id", "is_deleted", "occurred_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_financial_transaction_user_id_is_deleted_occurred_on",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_user_id_is_deleted",
                schema: "fortuna",
                table: "financial_transaction",
                columns: new[] { "user_id", "is_deleted" });
        }
    }
}
