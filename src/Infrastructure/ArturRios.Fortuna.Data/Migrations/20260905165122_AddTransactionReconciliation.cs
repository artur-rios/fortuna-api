using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "imported_record_id",
                schema: "fortuna",
                table: "financial_transaction",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "import_job",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    source_type = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_job", x => x.id);
                    table.CheckConstraint("ck_import_job_source_type", "source_type BETWEEN 2 AND 4");
                    table.CheckConstraint("ck_import_job_status", "status BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_import_job_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "imported_record",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    import_job_id = table.Column<long>(type: "bigint", nullable: false),
                    raw_payload = table.Column<string>(type: "jsonb", nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    outcome = table.Column<short>(type: "smallint", nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imported_record", x => x.id);
                    table.CheckConstraint("ck_imported_record_amount", "amount IS NULL OR amount > 0");
                    table.CheckConstraint("ck_imported_record_outcome", "outcome BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_imported_record_import_jobs_import_job_id",
                        column: x => x.import_job_id,
                        principalSchema: "fortuna",
                        principalTable: "import_job",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_imported_record_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "imported_record_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_job_public_id",
                schema: "fortuna",
                table: "import_job",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_job_user_id_status",
                schema: "fortuna",
                table: "import_job",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_imported_record_import_job_id",
                schema: "fortuna",
                table: "imported_record",
                column: "import_job_id");

            migrationBuilder.AddForeignKey(
                name: "fk_financial_transaction_imported_records_imported_record_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "imported_record_id",
                principalSchema: "fortuna",
                principalTable: "imported_record",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_financial_transaction_imported_records_imported_record_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropTable(
                name: "imported_record",
                schema: "fortuna");

            migrationBuilder.DropTable(
                name: "import_job",
                schema: "fortuna");

            migrationBuilder.DropIndex(
                name: "ix_financial_transaction_imported_record_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "imported_record_id",
                schema: "fortuna",
                table: "financial_transaction");
        }
    }
}
