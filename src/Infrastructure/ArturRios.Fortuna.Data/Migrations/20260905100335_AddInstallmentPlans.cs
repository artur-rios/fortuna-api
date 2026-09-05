using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallmentPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "installment_number",
                schema: "fortuna",
                table: "financial_transaction",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "installment_plan_id",
                schema: "fortuna",
                table: "financial_transaction",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "installment_plan",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    credit_card_id = table.Column<long>(type: "bigint", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    installment_count = table.Column<short>(type: "smallint", nullable: false),
                    purchased_on = table.Column<DateOnly>(type: "date", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installment_plan", x => x.id);
                    table.CheckConstraint("ck_installment_plan_count", "installment_count >= 2");
                    table.CheckConstraint("ck_installment_plan_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_installment_plan_total_amount", "total_amount > 0");
                    table.ForeignKey(
                        name: "fk_installment_plan_credit_card_credit_card_id",
                        column: x => x.credit_card_id,
                        principalSchema: "fortuna",
                        principalTable: "credit_card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_installment_plan_id_installment_number",
                schema: "fortuna",
                table: "financial_transaction",
                columns: new[] { "installment_plan_id", "installment_number" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_transaction_installment",
                schema: "fortuna",
                table: "financial_transaction",
                sql: "(installment_plan_id IS NULL AND installment_number IS NULL) OR (installment_plan_id IS NOT NULL AND installment_number >= 1)");

            migrationBuilder.CreateIndex(
                name: "ix_installment_plan_credit_card_id_is_deleted",
                schema: "fortuna",
                table: "installment_plan",
                columns: new[] { "credit_card_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_installment_plan_public_id",
                schema: "fortuna",
                table: "installment_plan",
                column: "public_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_financial_transaction_installment_plans_installment_plan_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "installment_plan_id",
                principalSchema: "fortuna",
                principalTable: "installment_plan",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_financial_transaction_installment_plans_installment_plan_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropTable(
                name: "installment_plan",
                schema: "fortuna");

            migrationBuilder.DropIndex(
                name: "ix_financial_transaction_installment_plan_id_installment_number",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_transaction_installment",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "installment_number",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "installment_plan_id",
                schema: "fortuna",
                table: "financial_transaction");
        }
    }
}
