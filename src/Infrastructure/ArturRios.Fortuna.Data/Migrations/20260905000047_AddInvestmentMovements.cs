using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transfer_movements",
                schema: "fortuna",
                table: "transfer");

            migrationBuilder.AlterColumn<long>(
                name: "inbound_transaction_id",
                schema: "fortuna",
                table: "transfer",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "inbound_investment_movement_id",
                schema: "fortuna",
                table: "transfer",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "investment_movement",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    investment_id = table.Column<long>(type: "bigint", nullable: false),
                    movement_type = table.Column<short>(type: "smallint", nullable: false),
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
                    table.PrimaryKey("pk_investment_movement", x => x.id);
                    table.CheckConstraint("ck_investment_movement_amount", "amount > 0");
                    table.CheckConstraint("ck_investment_movement_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_investment_movement_type", "movement_type BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_investment_movement_investment_investment_id",
                        column: x => x.investment_id,
                        principalSchema: "fortuna",
                        principalTable: "investment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_inbound_investment_movement_id",
                schema: "fortuna",
                table: "transfer",
                column: "inbound_investment_movement_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfer_movements",
                schema: "fortuna",
                table: "transfer",
                sql: "(inbound_transaction_id IS NOT NULL AND inbound_investment_movement_id IS NULL AND outbound_transaction_id <> inbound_transaction_id) OR (inbound_transaction_id IS NULL AND inbound_investment_movement_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_investment_movement_investment_id_is_deleted_occurred_on",
                schema: "fortuna",
                table: "investment_movement",
                columns: new[] { "investment_id", "is_deleted", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_investment_movement_public_id",
                schema: "fortuna",
                table: "investment_movement",
                column: "public_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_transfer_investment_movement_inbound_investment_movement_id",
                schema: "fortuna",
                table: "transfer",
                column: "inbound_investment_movement_id",
                principalSchema: "fortuna",
                principalTable: "investment_movement",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transfer_investment_movement_inbound_investment_movement_id",
                schema: "fortuna",
                table: "transfer");

            migrationBuilder.DropTable(
                name: "investment_movement",
                schema: "fortuna");

            migrationBuilder.DropIndex(
                name: "ix_transfer_inbound_investment_movement_id",
                schema: "fortuna",
                table: "transfer");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfer_movements",
                schema: "fortuna",
                table: "transfer");

            migrationBuilder.DropColumn(
                name: "inbound_investment_movement_id",
                schema: "fortuna",
                table: "transfer");

            migrationBuilder.AlterColumn<long>(
                name: "inbound_transaction_id",
                schema: "fortuna",
                table: "transfer",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfer_movements",
                schema: "fortuna",
                table: "transfer",
                sql: "outbound_transaction_id <> inbound_transaction_id");
        }
    }
}
