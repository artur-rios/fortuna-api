using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArturRios.Fortuna.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionRecording : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "category_id",
                schema: "fortuna",
                table: "financial_transaction",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "counterparty_id",
                schema: "fortuna",
                table: "financial_transaction",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "currency_id",
                schema: "fortuna",
                table: "financial_transaction",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "fortuna",
                table: "financial_transaction",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_reconciled",
                schema: "fortuna",
                table: "financial_transaction",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "source_type",
                schema: "fortuna",
                table: "financial_transaction",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.CreateTable(
                name: "category",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category", x => x.id);
                    table.CheckConstraint("ck_category_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_category_category_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "fortuna",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_category_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "counterparty",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_counterparty", x => x.id);
                    table.CheckConstraint("ck_counterparty_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_counterparty_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tag",
                schema: "fortuna",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tag", x => x.id);
                    table.CheckConstraint("ck_tag_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_tag_user_profiles_user_id",
                        column: x => x.user_id,
                        principalSchema: "fortuna",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_transaction_tag",
                schema: "fortuna",
                columns: table => new
                {
                    financial_transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    tag_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_transaction_tag", x => new { x.financial_transaction_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_financial_transaction_tag_financial_transaction_financial_t",
                        column: x => x.financial_transaction_id,
                        principalSchema: "fortuna",
                        principalTable: "financial_transaction",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_financial_transaction_tag_tags_tag_id",
                        column: x => x.tag_id,
                        principalSchema: "fortuna",
                        principalTable: "tag",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_category_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_counterparty_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "counterparty_id");

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_currency_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "currency_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_transaction_source_type",
                schema: "fortuna",
                table: "financial_transaction",
                sql: "source_type BETWEEN 1 AND 4");

            migrationBuilder.CreateIndex(
                name: "ix_category_parent_id",
                schema: "fortuna",
                table: "category",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_public_id",
                schema: "fortuna",
                table: "category",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_category_user_id_is_deleted",
                schema: "fortuna",
                table: "category",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_category_user_id_normalized_name",
                schema: "fortuna",
                table: "category",
                columns: new[] { "user_id", "normalized_name" },
                unique: true,
                filter: "parent_id IS NULL AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_category_user_id_parent_id_normalized_name",
                schema: "fortuna",
                table: "category",
                columns: new[] { "user_id", "parent_id", "normalized_name" },
                unique: true,
                filter: "parent_id IS NOT NULL AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_counterparty_public_id",
                schema: "fortuna",
                table: "counterparty",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_counterparty_user_id_is_deleted",
                schema: "fortuna",
                table: "counterparty",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_counterparty_user_id_normalized_name",
                schema: "fortuna",
                table: "counterparty",
                columns: new[] { "user_id", "normalized_name" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_tag_tag_id",
                schema: "fortuna",
                table: "financial_transaction_tag",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_tag_public_id",
                schema: "fortuna",
                table: "tag",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tag_user_id_is_deleted",
                schema: "fortuna",
                table: "tag",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_tag_user_id_normalized_name",
                schema: "fortuna",
                table: "tag",
                columns: new[] { "user_id", "normalized_name" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.Sql(
                """
                INSERT INTO fortuna.category
                    (public_id, user_id, name, normalized_name, is_deleted,
                     deletion_cascade_id, created_at, updated_at)
                SELECT gen_random_uuid(), user_id, 'Uncategorized', 'UNCATEGORIZED',
                       FALSE, NULL, MIN(created_at), MAX(updated_at)
                FROM fortuna.financial_transaction
                GROUP BY user_id;

                UPDATE fortuna.financial_transaction AS transaction
                SET category_id = category.id
                FROM fortuna.category AS category
                WHERE category.user_id = transaction.user_id
                  AND category.normalized_name = 'UNCATEGORIZED';

                UPDATE fortuna.financial_transaction AS transaction
                SET currency_id = account.currency_id
                FROM fortuna.financial_account AS account
                WHERE account.id = transaction.financial_account_id;

                UPDATE fortuna.financial_transaction AS transaction
                SET currency_id = card.currency_id
                FROM fortuna.credit_card AS card
                WHERE card.id = transaction.credit_card_id;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "category_id",
                schema: "fortuna",
                table: "financial_transaction",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 0L);

            migrationBuilder.AlterColumn<long>(
                name: "currency_id",
                schema: "fortuna",
                table: "financial_transaction",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 0L);

            migrationBuilder.AddForeignKey(
                name: "fk_financial_transaction_category_category_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "category_id",
                principalSchema: "fortuna",
                principalTable: "category",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_financial_transaction_counterparty_counterparty_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "counterparty_id",
                principalSchema: "fortuna",
                principalTable: "counterparty",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_financial_transaction_currency_currency_id",
                schema: "fortuna",
                table: "financial_transaction",
                column: "currency_id",
                principalSchema: "fortuna",
                principalTable: "currency",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_financial_transaction_category_category_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropForeignKey(
                name: "fk_financial_transaction_counterparty_counterparty_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropForeignKey(
                name: "fk_financial_transaction_currency_currency_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropTable(
                name: "category",
                schema: "fortuna");

            migrationBuilder.DropTable(
                name: "counterparty",
                schema: "fortuna");

            migrationBuilder.DropTable(
                name: "financial_transaction_tag",
                schema: "fortuna");

            migrationBuilder.DropTable(
                name: "tag",
                schema: "fortuna");

            migrationBuilder.DropIndex(
                name: "ix_financial_transaction_category_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropIndex(
                name: "ix_financial_transaction_counterparty_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropIndex(
                name: "ix_financial_transaction_currency_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_transaction_source_type",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "counterparty_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "currency_id",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "is_reconciled",
                schema: "fortuna",
                table: "financial_transaction");

            migrationBuilder.DropColumn(
                name: "source_type",
                schema: "fortuna",
                table: "financial_transaction");
        }
    }
}
