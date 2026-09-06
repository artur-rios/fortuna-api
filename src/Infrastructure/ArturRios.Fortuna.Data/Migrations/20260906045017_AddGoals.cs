using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "goal",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    target_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency_id = table.Column<long>(type: "bigint", nullable: false),
                    target_date = table.Column<DateOnly>(type: "date", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal", x => x.id);
                    table.CheckConstraint("ck_goal_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_goal_target_amount", "target_amount > 0");
                    table.ForeignKey(
                        name: "fk_goal_currency_currency_id",
                        column: x => x.currency_id,
                        principalSchema: "fortuna",
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goal_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goal_account",
                schema: "fortuna",
                columns: table => new
                {
                    goal_id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal_account", x => new { x.goal_id, x.account_id });
                    table.ForeignKey(
                        name: "fk_goal_account_financial_account_account_id",
                        column: x => x.account_id,
                        principalSchema: "fortuna",
                        principalTable: "financial_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goal_account_goal_goal_id",
                        column: x => x.goal_id,
                        principalSchema: "fortuna",
                        principalTable: "goal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "goal_investment",
                schema: "fortuna",
                columns: table => new
                {
                    goal_id = table.Column<long>(type: "bigint", nullable: false),
                    investment_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal_investment", x => new { x.goal_id, x.investment_id });
                    table.ForeignKey(
                        name: "fk_goal_investment_goal_goal_id",
                        column: x => x.goal_id,
                        principalSchema: "fortuna",
                        principalTable: "goal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_goal_investment_investments_investment_id",
                        column: x => x.investment_id,
                        principalSchema: "fortuna",
                        principalTable: "investment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_goal_currency_id",
                schema: "fortuna",
                table: "goal",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_public_id",
                schema: "fortuna",
                table: "goal",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goal_user_id_is_deleted",
                schema: "fortuna",
                table: "goal",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_goal_account_account_id",
                schema: "fortuna",
                table: "goal_account",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_investment_investment_id",
                schema: "fortuna",
                table: "goal_investment",
                column: "investment_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goal_account",
                schema: "fortuna");

            migrationBuilder.DropTable(
                name: "goal_investment",
                schema: "fortuna");

            migrationBuilder.DropTable(
                name: "goal",
                schema: "fortuna");
        }
    }
}
