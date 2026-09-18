namespace ArturRios.Fortuna.Data.Tests;

public sealed class SqliteReportingReaderTests(SqliteReportingFixture fixture)
    : ReportingReaderTests(fixture), IClassFixture<SqliteReportingFixture>;
