using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentValuations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "investment_valuation",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    investment_id = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    valued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_investment_valuation", x => x.id);
                    table.CheckConstraint("ck_investment_valuation_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_investment_valuation_investment_investment_id",
                        column: x => x.investment_id,
                        principalSchema: "fortuna",
                        principalTable: "investment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_investment_valuation_investment_id_is_deleted_valued_on",
                schema: "fortuna",
                table: "investment_valuation",
                columns: new[] { "investment_id", "is_deleted", "valued_on" });

            migrationBuilder.CreateIndex(
                name: "ix_investment_valuation_public_id",
                schema: "fortuna",
                table: "investment_valuation",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_investment_valuation_investment_valued_on_live",
                schema: "fortuna",
                table: "investment_valuation",
                columns: new[] { "investment_id", "valued_on" },
                unique: true,
                filter: "NOT is_deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "investment_valuation",
                schema: "fortuna");
        }
    }
}
