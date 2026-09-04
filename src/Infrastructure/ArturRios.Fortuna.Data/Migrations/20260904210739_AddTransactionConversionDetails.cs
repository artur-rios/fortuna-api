using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionConversionDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "applied_rate",
                schema: "fortuna",
                table: "financial_transaction",
                type: "numeric(19,8)",
                precision: 19,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "original_amount",
                schema: "fortuna",
                table: "financial_transaction",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "original_currency_id",
                schema: "fortuna",
                table: "financial_transaction",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "rate_date",
                schema: "fortuna",
                table: "financial_transaction",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_original_currency_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "original_currency_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_transaction_foreign_currency",
                schema: "fortuna",
                table: "financial_transaction",
                sql: "(original_amount IS NULL AND original_currency_id IS NULL AND applied_rate IS NULL AND rate_date IS NULL) OR (original_amount > 0 AND original_currency_id IS NOT NULL AND applied_rate > 0 AND rate_date IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_financial_transaction_currency_original_currency_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "original_currency_id",
                principalSchema: "fortuna",
                principalTable: "currency",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_financial_transaction_currency_original_currency_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropIndex(
                name: "ix_financial_transaction_original_currency_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_transaction_foreign_currency",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "applied_rate",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "original_amount",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "original_currency_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "rate_date",
                schema: "fortuna",
                table: "financial_transaction");
        }
    }
}
