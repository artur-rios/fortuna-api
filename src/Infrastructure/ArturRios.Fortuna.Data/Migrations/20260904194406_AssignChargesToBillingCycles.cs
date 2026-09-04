using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AssignChargesToBillingCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_late_arriving",
                schema: "fortuna",
                table: "financial_transaction",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "statement_id",
                schema: "fortuna",
                table: "financial_transaction",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "credit_card_statement",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    credit_card_id = table.Column<long>(type: "bigint", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    closing_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    previous_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    payments_received = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    purchase_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    foreign_tax_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    other_entries = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount_due = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    settlement_transaction_id = table.Column<long>(type: "bigint", nullable: true),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_card_statement", x => x.id);
                    table.CheckConstraint("ck_credit_card_statement_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_credit_card_statement_period", "period_start <= period_end AND closing_date = period_end AND due_date > closing_date");
                    table.CheckConstraint("ck_credit_card_statement_status", "status BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_credit_card_statement_credit_card_credit_card_id",
                        column: x => x.credit_card_id,
                        principalSchema: "fortuna",
                        principalTable: "credit_card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credit_card_statement_financial_transactions_settlement_tra",
                        column: x => x.settlement_transaction_id,
                        principalSchema: "fortuna",
                        principalTable: "financial_transaction",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_statement_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "statement_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_card_statement_credit_card_id_status",
                schema: "fortuna",
                table: "credit_card_statement",
                columns: new[] { "credit_card_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_card_statement_public_id",
                schema: "fortuna",
                table: "credit_card_statement",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_card_statement_settlement_transaction_id",
                schema: "fortuna",
                table: "credit_card_statement",
                column: "settlement_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ux_credit_card_statement_card_period",
                schema: "fortuna",
                table: "credit_card_statement",
                columns: new[] { "credit_card_id", "period_start", "period_end" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_financial_transaction_credit_card_statement_statement_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "statement_id",
                principalSchema: "fortuna",
                principalTable: "credit_card_statement",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_financial_transaction_credit_card_statement_statement_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropTable(
                name: "credit_card_statement",
                schema: "fortuna");

            migrationBuilder.DropIndex(
                name: "ix_financial_transaction_statement_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "is_late_arriving",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "statement_id",
                schema: "fortuna",
                table: "financial_transaction");
        }
    }
}
