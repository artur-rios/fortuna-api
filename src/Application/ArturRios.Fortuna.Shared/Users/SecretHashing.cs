using ArturRios.Util.Hashing;

namespace ArturRios.Fortuna.Shared.Users;

/// <summary>
/// The Argon2id cost every local-account secret derivation uses: the library's 600 MB / 16-lane default, unless
/// <see cref="InsecureTestVariable" /> asks for the cheapest cost Argon2id accepts.
/// </summary>
/// <remarks>
/// The switch exists for the test suite. A derivation at the production cost is 600 MB, and the CI agent's test
/// process grew past its memory limit and was killed. Nothing a test asserts depends on the cost, only on
/// derivation and verification agreeing, so the suite sets the variable in <c>tests/default.runsettings</c> and
/// derives through <see cref="Configuration" /> everywhere, fixtures included. Recovery codes have a lighter cost of
/// their own (<see cref="LocalRecoveryCodeHash" />), which the switch replaces too.
/// <para>
/// A switch rather than three numbers, and named for what it is, so that it cannot be half tuned: a stored hash can
/// only be verified with the cost it was made with, so a changed cost locks every local account out. Start-up
/// refuses it outside Development (<see cref="StartupError" />). Read once, when the type is first used, so every
/// derivation in the process agrees.
/// </para>
/// </remarks>
public static class SecretHashing
{
    /// <summary>The environment variable that, set to <c>true</c>, selects <see cref="Insecure" />.</summary>
    public const string InsecureTestVariable = "FORTUNA_INSECURE_TEST_SECRET_HASHING";

    /// <summary>The cheapest cost Argon2id accepts: one lane, one pass, 8 KB. For tests only.</summary>
    public static HashConfiguration Insecure { get; } = new(1, 1, 8);

    /// <summary>The cost in effect for this process.</summary>
    public static HashConfiguration Configuration { get; } = FromEnvironment(Environment.GetEnvironmentVariable);

    /// <summary>Whether <see cref="Configuration" /> is <see cref="Insecure" />.</summary>
    public static bool IsInsecure => ReferenceEquals(Configuration, Insecure);

    /// <summary>
    /// <see cref="Insecure" /> when <paramref name="read" /> gives <c>true</c> (ignoring case and surrounding space)
    /// for <see cref="InsecureTestVariable" />; the production cost for anything else, unset included.
    /// </summary>
    public static HashConfiguration FromEnvironment(Func<string, string?> read) =>
        string.Equals(read(InsecureTestVariable)?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            ? Insecure
            : new HashConfiguration();

    /// <summary>
    /// The start-up error <paramref name="read" />'s settings make: the insecure cost anywhere but Development, or
    /// <c>null</c> when they may run.
    /// </summary>
    /// <remarks>
    /// The environment is the one the process declares, as ASP.NET Core resolves it: <c>ASPNETCORE_ENVIRONMENT</c>,
    /// else <c>DOTNET_ENVIRONMENT</c>, else Production. Every deployment declares one (Production, homologation
    /// included), and the test suite declares Development in <c>tests/default.runsettings</c>, so a test host built
    /// for Production still starts with the switch on.
    /// </remarks>
    public static string? StartupError(Func<string, string?> read)
    {
        var environment = Declared(read("ASPNETCORE_ENVIRONMENT"))
                          ?? Declared(read("DOTNET_ENVIRONMENT"))
                          ?? "Production";

        return ReferenceEquals(FromEnvironment(read), Insecure)
               && !environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
            ? $"Environment variable '{InsecureTestVariable}' is set in the {environment} environment. It is for " +
              "the test suite only: it makes every local-account secret trivial to brute-force, and no secret " +
              "stored with it verifies once it is removed."
            : null;
    }

    private static string? Declared(string? environment) =>
        string.IsNullOrWhiteSpace(environment) ? null : environment.Trim();
}
