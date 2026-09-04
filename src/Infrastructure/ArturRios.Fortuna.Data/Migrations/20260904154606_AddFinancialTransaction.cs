using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_transaction",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    financial_account_id = table.Column<long>(type: "bigint", nullable: false),
                    direction = table.Column<short>(type: "smallint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_transaction", x => x.id);
                    table.CheckConstraint("ck_financial_transaction_amount", "amount > 0");
                    table.CheckConstraint("ck_financial_transaction_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_financial_transaction_direction", "direction BETWEEN 1 AND 2");
                    table.ForeignKey(
                        name: "fk_financial_transaction_financial_account_financial_account_id",
                        column: x => x.financial_account_id,
                        principalSchema: "fortuna",
                        principalTable: "financial_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_transaction_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_financial_account_id_is_deleted_occur",
                schema: "fortuna",
                table: "financial_transaction",
                columns: new[] { "financial_account_id", "is_deleted", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_public_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_user_id_is_deleted",
                schema: "fortuna",
                table: "financial_transaction",
                columns: new[] { "user_id", "is_deleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "financial_transaction",
                schema: "fortuna");
        }
    }
}
