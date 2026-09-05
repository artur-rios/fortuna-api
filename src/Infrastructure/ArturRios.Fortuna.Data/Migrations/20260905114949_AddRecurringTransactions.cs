using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recurring_transaction",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    financial_account_id = table.Column<long>(type: "bigint", nullable: true),
                    credit_card_id = table.Column<long>(type: "bigint", nullable: true),
                    category_id = table.Column<long>(type: "bigint", nullable: false),
                    counterparty_id = table.Column<long>(type: "bigint", nullable: true),
                    direction = table.Column<short>(type: "smallint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency_id = table.Column<long>(type: "bigint", nullable: false),
                    frequency = table.Column<short>(type: "smallint", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    last_materialized_on = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_transaction", x => x.id);
                    table.CheckConstraint("ck_recurring_transaction_amount", "amount > 0");
                    table.CheckConstraint("ck_recurring_transaction_dates", "ends_on IS NULL OR ends_on >= starts_on");
                    table.CheckConstraint("ck_recurring_transaction_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_recurring_transaction_direction", "direction BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_recurring_transaction_frequency", "frequency BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_recurring_transaction_target", "(financial_account_id IS NOT NULL AND credit_card_id IS NULL) OR (financial_account_id IS NULL AND credit_card_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_recurring_transaction_category_category_id",
                        column: x => x.category_id,
                        principalSchema: "fortuna",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_counterparty_counterparty_id",
                        column: x => x.counterparty_id,
                        principalSchema: "fortuna",
                        principalTable: "counterparty",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_credit_card_credit_card_id",
                        column: x => x.credit_card_id,
                        principalSchema: "fortuna",
                        principalTable: "credit_card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_currency_currency_id",
                        column: x => x.currency_id,
                        principalSchema: "fortuna",
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_financial_account_financial_account_id",
                        column: x => x.financial_account_id,
                        principalSchema: "fortuna",
                        principalTable: "financial_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_category_id",
                schema: "fortuna",
                table: "recurring_transaction",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_counterparty_id",
                schema: "fortuna",
                table: "recurring_transaction",
                column: "counterparty_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_credit_card_id",
                schema: "fortuna",
                table: "recurring_transaction",
                column: "credit_card_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_currency_id",
                schema: "fortuna",
                table: "recurring_transaction",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_financial_account_id",
                schema: "fortuna",
                table: "recurring_transaction",
                column: "financial_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_public_id",
                schema: "fortuna",
                table: "recurring_transaction",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_user_id_is_deleted",
                schema: "fortuna",
                table: "recurring_transaction",
                columns: new[] { "user_id", "is_deleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_transaction",
                schema: "fortuna");
        }
    }
}
