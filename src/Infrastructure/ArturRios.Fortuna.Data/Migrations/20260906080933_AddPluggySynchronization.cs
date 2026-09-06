using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPluggySynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "connection_id",
                schema: "fortuna",
                table: "import_job",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "duplicate_count",
                schema: "fortuna",
                table: "import_job",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                schema: "fortuna",
                table: "import_job",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "imported_count",
                schema: "fortuna",
                table: "import_job",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "period_end",
                schema: "fortuna",
                table: "import_job",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "period_start",
                schema: "fortuna",
                table: "import_job",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rejected_count",
                schema: "fortuna",
                table: "import_job",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "connection_resource",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    connection_id = table.Column<long>(type: "bigint", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    financial_account_id = table.Column<long>(type: "bigint", nullable: true),
                    credit_card_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connection_resource", x => x.id);
                    table.CheckConstraint("ck_connection_resource_target", "(financial_account_id IS NULL) <> (credit_card_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_connection_resource_connection_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "fortuna",
                        principalTable: "connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_connection_resource_credit_cards_credit_card_id",
                        column: x => x.credit_card_id,
                        principalSchema: "fortuna",
                        principalTable: "credit_card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_connection_resource_financial_accounts_financial_account_id",
                        column: x => x.financial_account_id,
                        principalSchema: "fortuna",
                        principalTable: "financial_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_job_connection_id_status",
                schema: "fortuna",
                table: "import_job",
                columns: new[] { "connection_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_import_job_connection_unfinished",
                schema: "fortuna",
                table: "import_job",
                column: "connection_id",
                unique: true,
                filter: "connection_id IS NOT NULL AND status IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_connection_resource_connection_id_external_reference",
                schema: "fortuna",
                table: "connection_resource",
                columns: new[] { "connection_id", "external_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_connection_resource_credit_card_id",
                schema: "fortuna",
                table: "connection_resource",
                column: "credit_card_id");

            migrationBuilder.CreateIndex(
                name: "ix_connection_resource_financial_account_id",
                schema: "fortuna",
                table: "connection_resource",
                column: "financial_account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_import_job_connection_connection_id",
                schema: "fortuna",
                table: "import_job",
                column: "connection_id",
                principalSchema: "fortuna",
                principalTable: "connection",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_import_job_connection_connection_id",
                schema: "fortuna",
                table: "import_job");

            migrationBuilder.DropTable(
                name: "connection_resource",
                schema: "fortuna");

            migrationBuilder.DropIndex(
                name: "ix_import_job_connection_id_status",
                schema: "fortuna",
                table: "import_job");

            migrationBuilder.DropIndex(
                name: "ux_import_job_connection_unfinished",
                schema: "fortuna",
                table: "import_job");

            migrationBuilder.DropColumn(
                name: "connection_id",
                schema: "fortuna",
                table: "import_job");

            migrationBuilder.DropColumn(
                name: "duplicate_count",
                schema: "fortuna",
                table: "import_job");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                schema: "fortuna",
                table: "import_job");

            migrationBuilder.DropColumn(
                name: "imported_count",
                schema: "fortuna",
                table: "import_job");

            migrationBuilder.DropColumn(
                name: "period_end",
                schema: "fortuna",
                table: "import_job");

            migrationBuilder.DropColumn(
                name: "period_start",
                schema: "fortuna",
                table: "import_job");

            migrationBuilder.DropColumn(
                name: "rejected_count",
                schema: "fortuna",
                table: "import_job");
        }
    }
}
