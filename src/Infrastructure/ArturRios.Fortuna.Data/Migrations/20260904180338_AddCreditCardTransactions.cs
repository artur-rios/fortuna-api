using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditCardTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "financial_account_id",
                schema: "fortuna",
                table: "financial_transaction",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "credit_card_id",
                schema: "fortuna",
                table: "financial_transaction",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_credit_card_id_is_deleted_occurred_on",
                schema: "fortuna",
                table: "financial_transaction",
                columns: new[] { "credit_card_id", "is_deleted", "occurred_on" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_transaction_target",
                schema: "fortuna",
                table: "financial_transaction",
                sql: "(financial_account_id IS NOT NULL AND credit_card_id IS NULL) OR (financial_account_id IS NULL AND credit_card_id IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_financial_transaction_credit_card_credit_card_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "credit_card_id",
                principalSchema: "fortuna",
                principalTable: "credit_card",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_financial_transaction_credit_card_credit_card_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropIndex(
                name: "ix_financial_transaction_credit_card_id_is_deleted_occurred_on",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_transaction_target",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "credit_card_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.AlterColumn<long>(
                name: "financial_account_id",
                schema: "fortuna",
                table: "financial_transaction",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
