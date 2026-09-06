using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budget",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency_id = table.Column<long>(type: "bigint", nullable: false),
                    period_type = table.Column<short>(type: "smallint", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    include_descendants = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget", x => x.id);
                    table.CheckConstraint("ck_budget_amount", "amount > 0");
                    table.CheckConstraint("ck_budget_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_budget_period_type", "period_type BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_budget_currencies_currency_id",
                        column: x => x.currency_id,
                        principalSchema: "fortuna",
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_budget_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "budget_category",
                schema: "fortuna",
                columns: table => new
                {
                    budget_id = table.Column<long>(type: "bigint", nullable: false),
                    category_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_category", x => new { x.budget_id, x.category_id });
                    table.ForeignKey(
                        name: "fk_budget_category_budget_budget_id",
                        column: x => x.budget_id,
                        principalSchema: "fortuna",
                        principalTable: "budget",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_budget_category_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "fortuna",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_budget_currency_id",
                schema: "fortuna",
                table: "budget",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_budget_public_id",
                schema: "fortuna",
                table: "budget",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_budget_user_id_is_deleted",
                schema: "fortuna",
                table: "budget",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_budget_category_category_id",
                schema: "fortuna",
                table: "budget_category",
                column: "category_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budget_category",
                schema: "fortuna");

            migrationBuilder.DropTable(
                name: "budget",
                schema: "fortuna");
        }
    }
}
