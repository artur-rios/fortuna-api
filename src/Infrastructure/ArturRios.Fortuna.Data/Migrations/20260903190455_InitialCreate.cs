using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fortuna");

            migrationBuilder.CreateTable(
                name: "background_job",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_background_job", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "currency",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "char(3)", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    minor_unit_digits = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currency", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rate",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    base_currency_id = table.Column<long>(type: "bigint", nullable: false),
                    quote_currency_id = table.Column<long>(type: "bigint", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rate", x => x.id);
                    table.CheckConstraint("ck_exchange_rate_distinct_currency", "base_currency_id <> quote_currency_id");
                    table.CheckConstraint("ck_exchange_rate_positive", "rate > 0");
                    table.ForeignKey(
                        name: "fk_exchange_rate_currency_base_currency_id",
                        column: x => x.base_currency_id,
                        principalSchema: "fortuna",
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exchange_rate_currency_quote_currency_id",
                        column: x => x.quote_currency_id,
                        principalSchema: "fortuna",
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_background_job_idempotency_key",
                schema: "fortuna",
                table: "background_job",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_background_job_state",
                schema: "fortuna",
                table: "background_job",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_currency_code",
                schema: "fortuna",
                table: "currency",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rate_base_currency_id_quote_currency_id_rate_date_",
                schema: "fortuna",
                table: "exchange_rate",
                columns: new[] { "base_currency_id", "quote_currency_id", "rate_date", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rate_quote_currency_id",
                schema: "fortuna",
                table: "exchange_rate",
                column: "quote_currency_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "background_job",
                schema: "fortuna");

            migrationBuilder.DropTable(
                name: "exchange_rate",
                schema: "fortuna");

            migrationBuilder.DropTable(
                name: "currency",
                schema: "fortuna");
        }
    }
}
