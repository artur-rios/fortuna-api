using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStatementSettlementTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transfer",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    outbound_transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    inbound_transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    applied_rate = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: true),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: true),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer", x => x.id);
                    table.CheckConstraint("ck_transfer_conversion", "(applied_rate IS NULL AND rate_date IS NULL) OR (applied_rate > 0 AND rate_date IS NOT NULL)");
                    table.CheckConstraint("ck_transfer_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_transfer_movements", "outbound_transaction_id <> inbound_transaction_id");
                    table.ForeignKey(
                        name: "fk_transfer_financial_transaction_inbound_transaction_id",
                        column: x => x.inbound_transaction_id,
                        principalSchema: "fortuna",
                        principalTable: "financial_transaction",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_transfer_financial_transaction_outbound_transaction_id",
                        column: x => x.outbound_transaction_id,
                        principalSchema: "fortuna",
                        principalTable: "financial_transaction",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_inbound_transaction_id",
                schema: "fortuna",
                table: "transfer",
                column: "inbound_transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfer_outbound_transaction_id",
                schema: "fortuna",
                table: "transfer",
                column: "outbound_transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfer_public_id",
                schema: "fortuna",
                table: "transfer",
                column: "public_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transfer",
                schema: "fortuna");
        }
    }
}
