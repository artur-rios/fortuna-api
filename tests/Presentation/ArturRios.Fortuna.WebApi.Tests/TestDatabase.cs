namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Connection strings for tests that must configure a database without reaching a real one.
/// </summary>
internal static class TestDatabase
{
    /// <summary>
    ///     A PostgreSQL connection string that no server answers: nothing listens on port 1 of the loopback
    ///     address, so a connection attempt is refused at once and never leaves the machine.
    /// </summary>
    /// <remarks>
    ///     The application refuses to start without <c>FORTUNA_DATA_CONNECTIONSTRING</c>, so every suite sets
    ///     one. A suite that starts a PostgreSQL Testcontainer replaces the database registration with the
    ///     container's connection string; every other suite keeps this one. It must never name the default
    ///     PostgreSQL port on a loopback host: on a machine that runs PostgreSQL there, as the production
    ///     host does, the audit writer and user-profile provisioning would log in to it on every request.
    ///     <see cref="TestDatabaseGuardTests" /> fails if a test source does.
    /// </remarks>
    public const string Unreachable =
        "Host=127.0.0.1;Port=1;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna";
}
