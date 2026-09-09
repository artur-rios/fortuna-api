using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArturRios.Fortuna.Data.Sqlite.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entry",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    actor_user_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    operation = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    entity_type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    entity_public_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    outcome = table.Column<short>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "background_job",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false),
                    idempotency_key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    state = table.Column<short>(type: "INTEGER", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    failure_reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_background_job", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "currency",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    code = table.Column<string>(type: "char(3)", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    minor_unit_digits = table.Column<short>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currency", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    friendly_name = table.Column<string>(type: "TEXT", nullable: true),
                    xml = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rate",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    base_currency_id = table.Column<long>(type: "INTEGER", nullable: false),
                    quote_currency_id = table.Column<long>(type: "INTEGER", nullable: false),
                    rate = table.Column<decimal>(type: "TEXT", precision: 19, scale: 8, nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<short>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rate", x => x.id);
                    table.CheckConstraint("ck_exchange_rate_distinct_currency", "base_currency_id <> quote_currency_id");
                    table.CheckConstraint("ck_exchange_rate_positive", "rate > 0");
                    table.ForeignKey(
                        name: "fk_exchange_rate_currency_base_currency_id",
                        column: x => x.base_currency_id,
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exchange_rate_currency_quote_currency_id",
                        column: x => x.quote_currency_id,
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    external_subject = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    display_currency_id = table.Column<long>(type: "INTEGER", nullable: false),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user", x => x.id);
                    table.CheckConstraint("ck_user_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_user_currency_display_currency_id",
                        column: x => x.display_currency_id,
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "budget",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    currency_id = table.Column<long>(type: "INTEGER", nullable: false),
                    period_type = table.Column<short>(type: "INTEGER", nullable: false),
                    period_start = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    include_descendants = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
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
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_budget_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "category",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    parent_id = table.Column<long>(type: "INTEGER", nullable: true),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category", x => x.id);
                    table.CheckConstraint("ck_category_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_category_category_parent_id",
                        column: x => x.parent_id,
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_category_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "connection",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    data_source_type = table.Column<short>(type: "INTEGER", nullable: false),
                    external_reference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    access_token_cipher = table.Column<byte[]>(type: "BLOB", nullable: false),
                    status = table.Column<short>(type: "INTEGER", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connection", x => x.id);
                    table.ForeignKey(
                        name: "fk_connection_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "counterparty",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_counterparty", x => x.id);
                    table.CheckConstraint("ck_counterparty_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_counterparty_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "credit_card",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    issuer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    currency_id = table.Column<long>(type: "INTEGER", nullable: false),
                    credit_limit = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    closing_day = table.Column<short>(type: "INTEGER", nullable: false),
                    due_day = table.Column<short>(type: "INTEGER", nullable: false),
                    last_four_digits = table.Column<string>(type: "char(4)", nullable: true),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_card", x => x.id);
                    table.CheckConstraint("ck_credit_card_closing_day", "closing_day BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_credit_card_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_credit_card_due_day", "due_day BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_credit_card_last_four_digits", "last_four_digits IS NULL OR (length(last_four_digits) = 4 AND last_four_digits NOT GLOB '*[^0-9]*')");
                    table.CheckConstraint("ck_credit_card_limit", "credit_limit > 0");
                    table.ForeignKey(
                        name: "fk_credit_card_currencies_currency_id",
                        column: x => x.currency_id,
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credit_card_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "data_export",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    background_job_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    format = table.Column<short>(type: "INTEGER", nullable: false),
                    status = table.Column<short>(type: "INTEGER", nullable: false),
                    locale = table.Column<string>(type: "TEXT", maxLength: 35, nullable: false),
                    file_name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    request_json = table.Column<string>(type: "TEXT", nullable: false),
                    row_count = table.Column<int>(type: "INTEGER", nullable: true),
                    content_type = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    storage_key = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    failure_reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false),
                    expires_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_export", x => x.id);
                    table.CheckConstraint("ck_data_export_format", "format BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_data_export_row_count", "row_count IS NULL OR row_count >= 0");
                    table.CheckConstraint("ck_data_export_status", "status BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_data_export_background_job_background_job_id",
                        column: x => x.background_job_id,
                        principalTable: "background_job",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_export_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_account",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    institution = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    account_type = table.Column<short>(type: "INTEGER", nullable: false),
                    currency_id = table.Column<long>(type: "INTEGER", nullable: false),
                    opening_balance = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_account", x => x.id);
                    table.CheckConstraint("ck_financial_account_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_financial_account_type", "account_type BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_financial_account_currency_currency_id",
                        column: x => x.currency_id,
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_account_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goal",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    target_amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    currency_id = table.Column<long>(type: "INTEGER", nullable: false),
                    target_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal", x => x.id);
                    table.CheckConstraint("ck_goal_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_goal_target_amount", "target_amount > 0");
                    table.ForeignKey(
                        name: "fk_goal_currency_currency_id",
                        column: x => x.currency_id,
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goal_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "investment",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    instrument = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    normalized_instrument = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    institution = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    investment_type = table.Column<short>(type: "INTEGER", nullable: false),
                    currency_id = table.Column<long>(type: "INTEGER", nullable: false),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_investment", x => x.id);
                    table.CheckConstraint("ck_investment_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_investment_type", "investment_type BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_investment_currency_currency_id",
                        column: x => x.currency_id,
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_investment_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "local_account",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    secret_hash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    salt = table.Column<byte[]>(type: "BLOB", nullable: false),
                    storage_mode = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_local_account", x => x.id);
                    table.ForeignKey(
                        name: "fk_local_account_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tag",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tag", x => x.id);
                    table.CheckConstraint("ck_tag_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_tag_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "budget_category",
                columns: table => new
                {
                    budget_id = table.Column<long>(type: "INTEGER", nullable: false),
                    category_id = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_category", x => new { x.budget_id, x.category_id });
                    table.ForeignKey(
                        name: "fk_budget_category_budget_budget_id",
                        column: x => x.budget_id,
                        principalTable: "budget",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_budget_category_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "import_job",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    connection_id = table.Column<long>(type: "INTEGER", nullable: true),
                    source_type = table.Column<short>(type: "INTEGER", nullable: false),
                    status = table.Column<short>(type: "INTEGER", nullable: false),
                    period_start = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    period_end = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    imported_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    duplicate_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    rejected_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    failure_reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_job", x => x.id);
                    table.CheckConstraint("ck_import_job_source_type", "source_type BETWEEN 2 AND 4");
                    table.CheckConstraint("ck_import_job_status", "status BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_import_job_connection_connection_id",
                        column: x => x.connection_id,
                        principalTable: "connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_import_job_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "installment_plan",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    credit_card_id = table.Column<long>(type: "INTEGER", nullable: false),
                    total_amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    installment_count = table.Column<short>(type: "INTEGER", nullable: false),
                    purchased_on = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installment_plan", x => x.id);
                    table.CheckConstraint("ck_installment_plan_count", "installment_count >= 2");
                    table.CheckConstraint("ck_installment_plan_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_installment_plan_total_amount", "total_amount > 0");
                    table.ForeignKey(
                        name: "fk_installment_plan_credit_card_credit_card_id",
                        column: x => x.credit_card_id,
                        principalTable: "credit_card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "connection_resource",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    connection_id = table.Column<long>(type: "INTEGER", nullable: false),
                    external_reference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    financial_account_id = table.Column<long>(type: "INTEGER", nullable: true),
                    credit_card_id = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connection_resource", x => x.id);
                    table.CheckConstraint("ck_connection_resource_target", "(financial_account_id IS NULL) <> (credit_card_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_connection_resource_connection_connection_id",
                        column: x => x.connection_id,
                        principalTable: "connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_connection_resource_credit_cards_credit_card_id",
                        column: x => x.credit_card_id,
                        principalTable: "credit_card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_connection_resource_financial_accounts_financial_account_id",
                        column: x => x.financial_account_id,
                        principalTable: "financial_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recurring_transaction",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    financial_account_id = table.Column<long>(type: "INTEGER", nullable: true),
                    credit_card_id = table.Column<long>(type: "INTEGER", nullable: true),
                    category_id = table.Column<long>(type: "INTEGER", nullable: false),
                    counterparty_id = table.Column<long>(type: "INTEGER", nullable: true),
                    direction = table.Column<short>(type: "INTEGER", nullable: false),
                    amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    currency_id = table.Column<long>(type: "INTEGER", nullable: false),
                    frequency = table.Column<short>(type: "INTEGER", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    last_materialized_on = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_transaction", x => x.id);
                    table.CheckConstraint("ck_recurring_transaction_amount", "amount > 0");
                    table.CheckConstraint("ck_recurring_transaction_dates", "ends_on IS NULL OR ends_on >= starts_on");
                    table.CheckConstraint("ck_recurring_transaction_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_recurring_transaction_direction", "direction BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_recurring_transaction_frequency", "frequency BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_recurring_transaction_target", "(financial_account_id IS NOT NULL AND credit_card_id IS NULL) OR (financial_account_id IS NULL AND credit_card_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_recurring_transaction_category_category_id",
                        column: x => x.category_id,
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_counterparty_counterparty_id",
                        column: x => x.counterparty_id,
                        principalTable: "counterparty",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_credit_card_credit_card_id",
                        column: x => x.credit_card_id,
                        principalTable: "credit_card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_currency_currency_id",
                        column: x => x.currency_id,
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_financial_account_financial_account_id",
                        column: x => x.financial_account_id,
                        principalTable: "financial_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurring_transaction_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goal_account",
                columns: table => new
                {
                    goal_id = table.Column<long>(type: "INTEGER", nullable: false),
                    account_id = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal_account", x => new { x.goal_id, x.account_id });
                    table.ForeignKey(
                        name: "fk_goal_account_financial_account_account_id",
                        column: x => x.account_id,
                        principalTable: "financial_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goal_account_goal_goal_id",
                        column: x => x.goal_id,
                        principalTable: "goal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "goal_investment",
                columns: table => new
                {
                    goal_id = table.Column<long>(type: "INTEGER", nullable: false),
                    investment_id = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal_investment", x => new { x.goal_id, x.investment_id });
                    table.ForeignKey(
                        name: "fk_goal_investment_goal_goal_id",
                        column: x => x.goal_id,
                        principalTable: "goal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_goal_investment_investments_investment_id",
                        column: x => x.investment_id,
                        principalTable: "investment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "investment_movement",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    investment_id = table.Column<long>(type: "INTEGER", nullable: false),
                    movement_type = table.Column<short>(type: "INTEGER", nullable: false),
                    amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
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
                        principalTable: "investment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "investment_valuation",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    investment_id = table.Column<long>(type: "INTEGER", nullable: false),
                    value = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    valued_on = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_investment_valuation", x => x.id);
                    table.CheckConstraint("ck_investment_valuation_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_investment_valuation_investment_investment_id",
                        column: x => x.investment_id,
                        principalTable: "investment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recovery_code",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    local_account_id = table.Column<long>(type: "INTEGER", nullable: false),
                    code_hash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recovery_code", x => x.id);
                    table.ForeignKey(
                        name: "fk_recovery_code_local_account_local_account_id",
                        column: x => x.local_account_id,
                        principalTable: "local_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "imported_record",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    import_job_id = table.Column<long>(type: "INTEGER", nullable: false),
                    raw_payload = table.Column<string>(type: "TEXT", nullable: false),
                    external_id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    outcome = table.Column<short>(type: "INTEGER", nullable: false),
                    rejection_reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    occurred_on = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imported_record", x => x.id);
                    table.CheckConstraint("ck_imported_record_amount", "amount IS NULL OR amount > 0");
                    table.CheckConstraint("ck_imported_record_outcome", "outcome BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_imported_record_import_jobs_import_job_id",
                        column: x => x.import_job_id,
                        principalTable: "import_job",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attachment",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    transaction_id = table.Column<long>(type: "INTEGER", nullable: false),
                    file_name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    content_type = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    size_in_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    storage_key = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachment", x => x.id);
                    table.CheckConstraint("ck_attachment_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "credit_card_statement",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    credit_card_id = table.Column<long>(type: "INTEGER", nullable: false),
                    period_start = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    period_end = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    closing_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    due_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    previous_balance = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    payments_received = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    purchase_total = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    foreign_tax_total = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    other_entries = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    amount_due = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    status = table.Column<short>(type: "INTEGER", nullable: false),
                    settlement_transaction_id = table.Column<long>(type: "INTEGER", nullable: true),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_card_statement", x => x.id);
                    table.CheckConstraint("ck_credit_card_statement_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_credit_card_statement_period", "period_start <= period_end AND closing_date = period_end AND due_date > closing_date");
                    table.CheckConstraint("ck_credit_card_statement_status", "status BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_credit_card_statement_credit_card_credit_card_id",
                        column: x => x.credit_card_id,
                        principalTable: "credit_card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_transaction",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    financial_account_id = table.Column<long>(type: "INTEGER", nullable: true),
                    credit_card_id = table.Column<long>(type: "INTEGER", nullable: true),
                    statement_id = table.Column<long>(type: "INTEGER", nullable: true),
                    installment_plan_id = table.Column<long>(type: "INTEGER", nullable: true),
                    installment_number = table.Column<short>(type: "INTEGER", nullable: true),
                    recurring_transaction_id = table.Column<long>(type: "INTEGER", nullable: true),
                    imported_record_id = table.Column<long>(type: "INTEGER", nullable: true),
                    category_id = table.Column<long>(type: "INTEGER", nullable: false),
                    counterparty_id = table.Column<long>(type: "INTEGER", nullable: true),
                    direction = table.Column<short>(type: "INTEGER", nullable: false),
                    amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    currency_id = table.Column<long>(type: "INTEGER", nullable: false),
                    original_amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    original_currency_id = table.Column<long>(type: "INTEGER", nullable: true),
                    applied_rate = table.Column<decimal>(type: "TEXT", precision: 19, scale: 8, nullable: true),
                    rate_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    occurred_on = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    source_type = table.Column<short>(type: "INTEGER", nullable: false, defaultValue: (short)1),
                    is_reconciled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    is_manually_corrected = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    is_late_arriving = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    is_possible_duplicate = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_transaction", x => x.id);
                    table.CheckConstraint("ck_financial_transaction_amount", "amount > 0");
                    table.CheckConstraint("ck_financial_transaction_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_financial_transaction_direction", "direction BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_financial_transaction_foreign_currency", "(original_amount IS NULL AND original_currency_id IS NULL AND applied_rate IS NULL AND rate_date IS NULL) OR (original_amount > 0 AND original_currency_id IS NOT NULL AND applied_rate > 0 AND rate_date IS NOT NULL)");
                    table.CheckConstraint("ck_financial_transaction_installment", "(installment_plan_id IS NULL AND installment_number IS NULL) OR (installment_plan_id IS NOT NULL AND installment_number >= 1)");
                    table.CheckConstraint("ck_financial_transaction_source_type", "source_type BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_financial_transaction_target", "(financial_account_id IS NOT NULL AND credit_card_id IS NULL) OR (financial_account_id IS NULL AND credit_card_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_financial_transaction_category_category_id",
                        column: x => x.category_id,
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_transaction_counterparty_counterparty_id",
                        column: x => x.counterparty_id,
                        principalTable: "counterparty",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_transaction_credit_card_credit_card_id",
                        column: x => x.credit_card_id,
                        principalTable: "credit_card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_transaction_credit_card_statement_statement_id",
                        column: x => x.statement_id,
                        principalTable: "credit_card_statement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_transaction_currency_currency_id",
                        column: x => x.currency_id,
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_transaction_currency_original_currency_id",
                        column: x => x.original_currency_id,
                        principalTable: "currency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_transaction_financial_account_financial_account_id",
                        column: x => x.financial_account_id,
                        principalTable: "financial_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_transaction_imported_records_imported_record_id",
                        column: x => x.imported_record_id,
                        principalTable: "imported_record",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_transaction_installment_plans_installment_plan_id",
                        column: x => x.installment_plan_id,
                        principalTable: "installment_plan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_financial_transaction_recurring_transactions_recurring_transaction_id",
                        column: x => x.recurring_transaction_id,
                        principalTable: "recurring_transaction",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_transaction_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_transaction_tag",
                columns: table => new
                {
                    financial_transaction_id = table.Column<long>(type: "INTEGER", nullable: false),
                    tag_id = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_transaction_tag", x => new { x.financial_transaction_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_financial_transaction_tag_financial_transaction_financial_transaction_id",
                        column: x => x.financial_transaction_id,
                        principalTable: "financial_transaction",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_financial_transaction_tag_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tag",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    outbound_transaction_id = table.Column<long>(type: "INTEGER", nullable: false),
                    inbound_transaction_id = table.Column<long>(type: "INTEGER", nullable: true),
                    inbound_investment_movement_id = table.Column<long>(type: "INTEGER", nullable: true),
                    applied_rate = table.Column<decimal>(type: "TEXT", precision: 19, scale: 8, nullable: true),
                    rate_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    public_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    deletion_cascade_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer", x => x.id);
                    table.CheckConstraint("ck_transfer_conversion", "(applied_rate IS NULL AND rate_date IS NULL) OR (applied_rate > 0 AND rate_date IS NOT NULL)");
                    table.CheckConstraint("ck_transfer_deletion_state", "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)");
                    table.CheckConstraint("ck_transfer_movements", "(inbound_transaction_id IS NOT NULL AND inbound_investment_movement_id IS NULL AND outbound_transaction_id <> inbound_transaction_id) OR (inbound_transaction_id IS NULL AND inbound_investment_movement_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_transfer_financial_transaction_inbound_transaction_id",
                        column: x => x.inbound_transaction_id,
                        principalTable: "financial_transaction",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_transfer_financial_transaction_outbound_transaction_id",
                        column: x => x.outbound_transaction_id,
                        principalTable: "financial_transaction",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_transfer_investment_movement_inbound_investment_movement_id",
                        column: x => x.inbound_investment_movement_id,
                        principalTable: "investment_movement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachment_public_id",
                table: "attachment",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attachment_transaction_id_is_deleted",
                table: "attachment",
                columns: new[] { "transaction_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_actor_user_id",
                table: "audit_entry",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_occurred_at",
                table: "audit_entry",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_background_job_idempotency_key",
                table: "background_job",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_background_job_state",
                table: "background_job",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_budget_currency_id",
                table: "budget",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_budget_public_id",
                table: "budget",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_budget_user_id_is_deleted",
                table: "budget",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_budget_category_category_id",
                table: "budget_category",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_parent_id",
                table: "category",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_public_id",
                table: "category",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_category_user_id_is_deleted",
                table: "category",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_category_user_id_normalized_name",
                table: "category",
                columns: new[] { "user_id", "normalized_name" },
                unique: true,
                filter: "parent_id IS NULL AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_category_user_id_parent_id_normalized_name",
                table: "category",
                columns: new[] { "user_id", "parent_id", "normalized_name" },
                unique: true,
                filter: "parent_id IS NOT NULL AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_connection_public_id",
                table: "connection",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_connection_user_id_data_source_type_external_reference",
                table: "connection",
                columns: new[] { "user_id", "data_source_type", "external_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_connection_resource_connection_id_external_reference",
                table: "connection_resource",
                columns: new[] { "connection_id", "external_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_connection_resource_credit_card_id",
                table: "connection_resource",
                column: "credit_card_id");

            migrationBuilder.CreateIndex(
                name: "ix_connection_resource_financial_account_id",
                table: "connection_resource",
                column: "financial_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_counterparty_public_id",
                table: "counterparty",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_counterparty_user_id_is_deleted",
                table: "counterparty",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_counterparty_user_id_normalized_name",
                table: "counterparty",
                columns: new[] { "user_id", "normalized_name" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_credit_card_currency_id",
                table: "credit_card",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_card_public_id",
                table: "credit_card",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_card_user_id_is_deleted",
                table: "credit_card",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ux_credit_card_user_normalized_name_live",
                table: "credit_card",
                columns: new[] { "user_id", "normalized_name" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_credit_card_statement_credit_card_id_status",
                table: "credit_card_statement",
                columns: new[] { "credit_card_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_card_statement_public_id",
                table: "credit_card_statement",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_card_statement_settlement_transaction_id",
                table: "credit_card_statement",
                column: "settlement_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ux_credit_card_statement_card_period",
                table: "credit_card_statement",
                columns: new[] { "credit_card_id", "period_start", "period_end" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_currency_code",
                table: "currency",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_export_background_job_id",
                table: "data_export",
                column: "background_job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_export_public_id",
                table: "data_export",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_export_user_id_created_at",
                table: "data_export",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rate_base_currency_id_quote_currency_id_rate_date_source",
                table: "exchange_rate",
                columns: new[] { "base_currency_id", "quote_currency_id", "rate_date", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rate_quote_currency_id",
                table: "exchange_rate",
                column: "quote_currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_financial_account_currency_id",
                table: "financial_account",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_financial_account_public_id",
                table: "financial_account",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_account_user_id_is_deleted",
                table: "financial_account",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_account_user_normalized_name_live",
                table: "financial_account",
                columns: new[] { "user_id", "normalized_name" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_category_id",
                table: "financial_transaction",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_counterparty_id",
                table: "financial_transaction",
                column: "counterparty_id");

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_credit_card_id_is_deleted_occurred_on",
                table: "financial_transaction",
                columns: new[] { "credit_card_id", "is_deleted", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_currency_id",
                table: "financial_transaction",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_financial_account_id_is_deleted_occurred_on",
                table: "financial_transaction",
                columns: new[] { "financial_account_id", "is_deleted", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_imported_record_id",
                table: "financial_transaction",
                column: "imported_record_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_installment_plan_id_installment_number",
                table: "financial_transaction",
                columns: new[] { "installment_plan_id", "installment_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_original_currency_id",
                table: "financial_transaction",
                column: "original_currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_public_id",
                table: "financial_transaction",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_recurring_transaction_id_occurred_on",
                table: "financial_transaction",
                columns: new[] { "recurring_transaction_id", "occurred_on" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_statement_id",
                table: "financial_transaction",
                column: "statement_id");

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_user_id_is_deleted",
                table: "financial_transaction",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transaction_tag_tag_id",
                table: "financial_transaction_tag",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_currency_id",
                table: "goal",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_public_id",
                table: "goal",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goal_user_id_is_deleted",
                table: "goal",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_goal_account_account_id",
                table: "goal_account",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_investment_investment_id",
                table: "goal_investment",
                column: "investment_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_job_connection_id_status",
                table: "import_job",
                columns: new[] { "connection_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_import_job_public_id",
                table: "import_job",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_job_user_id_status",
                table: "import_job",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_import_job_connection_unfinished",
                table: "import_job",
                column: "connection_id",
                unique: true,
                filter: "connection_id IS NOT NULL AND status IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_imported_record_import_job_id",
                table: "imported_record",
                column: "import_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_installment_plan_credit_card_id_is_deleted",
                table: "installment_plan",
                columns: new[] { "credit_card_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_installment_plan_public_id",
                table: "installment_plan",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_investment_currency_id",
                table: "investment",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_investment_public_id",
                table: "investment",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_investment_user_id_is_deleted",
                table: "investment",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ux_investment_user_normalized_instrument_live",
                table: "investment",
                columns: new[] { "user_id", "normalized_instrument" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_investment_movement_investment_id_is_deleted_occurred_on",
                table: "investment_movement",
                columns: new[] { "investment_id", "is_deleted", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_investment_movement_public_id",
                table: "investment_movement",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_investment_valuation_investment_id_is_deleted_valued_on",
                table: "investment_valuation",
                columns: new[] { "investment_id", "is_deleted", "valued_on" });

            migrationBuilder.CreateIndex(
                name: "ix_investment_valuation_public_id",
                table: "investment_valuation",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_investment_valuation_investment_valued_on_live",
                table: "investment_valuation",
                columns: new[] { "investment_id", "valued_on" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_local_account_name",
                table: "local_account",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_account_public_id",
                table: "local_account",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_account_user_id",
                table: "local_account",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recovery_code_local_account_id_code_hash",
                table: "recovery_code",
                columns: new[] { "local_account_id", "code_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_category_id",
                table: "recurring_transaction",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_counterparty_id",
                table: "recurring_transaction",
                column: "counterparty_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_credit_card_id",
                table: "recurring_transaction",
                column: "credit_card_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_currency_id",
                table: "recurring_transaction",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_financial_account_id",
                table: "recurring_transaction",
                column: "financial_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_public_id",
                table: "recurring_transaction",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transaction_user_id_is_deleted",
                table: "recurring_transaction",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_tag_public_id",
                table: "tag",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tag_user_id_is_deleted",
                table: "tag",
                columns: new[] { "user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_tag_user_id_normalized_name",
                table: "tag",
                columns: new[] { "user_id", "normalized_name" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_inbound_investment_movement_id",
                table: "transfer",
                column: "inbound_investment_movement_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfer_inbound_transaction_id",
                table: "transfer",
                column: "inbound_transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfer_outbound_transaction_id",
                table: "transfer",
                column: "outbound_transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfer_public_id",
                table: "transfer",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_display_currency_id",
                table: "user",
                column: "display_currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_public_id",
                table: "user",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_user_external_subject",
                table: "user",
                column: "external_subject",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_attachment_financial_transactions_transaction_id",
                table: "attachment",
                column: "transaction_id",
                principalTable: "financial_transaction",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_credit_card_statement_financial_transactions_settlement_transaction_id",
                table: "credit_card_statement",
                column: "settlement_transaction_id",
                principalTable: "financial_transaction",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_credit_card_statement_financial_transactions_settlement_transaction_id",
                table: "credit_card_statement");

            migrationBuilder.DropTable(
                name: "attachment");

            migrationBuilder.DropTable(
                name: "audit_entry");

            migrationBuilder.DropTable(
                name: "budget_category");

            migrationBuilder.DropTable(
                name: "connection_resource");

            migrationBuilder.DropTable(
                name: "data_export");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "exchange_rate");

            migrationBuilder.DropTable(
                name: "financial_transaction_tag");

            migrationBuilder.DropTable(
                name: "goal_account");

            migrationBuilder.DropTable(
                name: "goal_investment");

            migrationBuilder.DropTable(
                name: "investment_valuation");

            migrationBuilder.DropTable(
                name: "recovery_code");

            migrationBuilder.DropTable(
                name: "transfer");

            migrationBuilder.DropTable(
                name: "budget");

            migrationBuilder.DropTable(
                name: "background_job");

            migrationBuilder.DropTable(
                name: "tag");

            migrationBuilder.DropTable(
                name: "goal");

            migrationBuilder.DropTable(
                name: "local_account");

            migrationBuilder.DropTable(
                name: "investment_movement");

            migrationBuilder.DropTable(
                name: "investment");

            migrationBuilder.DropTable(
                name: "financial_transaction");

            migrationBuilder.DropTable(
                name: "credit_card_statement");

            migrationBuilder.DropTable(
                name: "imported_record");

            migrationBuilder.DropTable(
                name: "installment_plan");

            migrationBuilder.DropTable(
                name: "recurring_transaction");

            migrationBuilder.DropTable(
                name: "import_job");

            migrationBuilder.DropTable(
                name: "category");

            migrationBuilder.DropTable(
                name: "counterparty");

            migrationBuilder.DropTable(
                name: "credit_card");

            migrationBuilder.DropTable(
                name: "financial_account");

            migrationBuilder.DropTable(
                name: "connection");

            migrationBuilder.DropTable(
                name: "user");

            migrationBuilder.DropTable(
                name: "currency");
        }
    }
}
