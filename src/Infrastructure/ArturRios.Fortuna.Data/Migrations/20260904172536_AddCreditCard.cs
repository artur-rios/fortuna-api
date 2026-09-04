using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credit_card",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    issuer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency_id = table.Column<long>(type: "bigint", nullable: false),
                    credit_limit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    closing_day = table.Column<short>(type: "smallint", nullable: false),
                    due_day = table.Column<short>(type: "smallint", nullable: false),
                    last_four_digits = table.Column<string>(type: "char(4)", nullable: true),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_card", x => x.id);
                    table.CheckConstraint("ck_credit_card_closing_day", "closing_day BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_credit_card_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_credit_card_due_day", "due_day BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_credit_card_last_four_digits", "last_four_digits IS NULL OR last_four_digits ~ '^[0-9]{4}$'");
                    table.CheckConstraint("ck_credit_card_limit", "credit_limit > 0");
                    table.ForeignKey(
                        name: "fk_credit_card_currencies_currency_id",
                        column: x => x.currency_id,
                        principalSchema: "fortuna",
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credit_card_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credit_card_currency_id",
                schema: "fortuna",
                table: "credit_card",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_card_public_id",
                schema: "fortuna",
                table: "credit_card",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_card_user_id_is_deleted",
                schema: "fortuna",
                table: "credit_card",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ux_credit_card_user_normalized_name_live",
                schema: "fortuna",
                table: "credit_card",
                columns: new[] { "user_id", "normalized_name" },
                unique: true,
                filter: "NOT is_deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_card",
                schema: "fortuna");
        }
    }
}
