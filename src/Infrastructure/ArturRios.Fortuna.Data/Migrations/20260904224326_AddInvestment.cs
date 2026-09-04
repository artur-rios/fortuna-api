using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "investment",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    instrument = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_instrument = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    institution = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    investment_type = table.Column<short>(type: "smallint", nullable: false),
                    currency_id = table.Column<long>(type: "bigint", nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_investment", x => x.id);
                    table.CheckConstraint("ck_investment_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_investment_type", "investment_type BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_investment_currency_currency_id",
                        column: x => x.currency_id,
                        principalSchema: "fortuna",
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_investment_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_investment_currency_id",
                schema: "fortuna",
                table: "investment",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_investment_public_id",
                schema: "fortuna",
                table: "investment",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_investment_user_id_is_deleted",
                schema: "fortuna",
                table: "investment",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ux_investment_user_normalized_instrument_live",
                schema: "fortuna",
                table: "investment",
                columns: new[] { "user_id", "normalized_instrument" },
                unique: true,
                filter: "NOT is_deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "investment",
                schema: "fortuna");
        }
    }
}
