namespace ArturRios.Fortuna.Data.Tests;

public sealed class PostgreSqlReportingReaderTests(PostgreSqlReportingFixture fixture)
    : ReportingReaderTests(fixture), IClassFixture<PostgreSqlReportingFixture>;
