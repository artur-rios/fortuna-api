using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class AuthenticationTests
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";

    [UnitFact]
    public void GivenSigningKeyMissing_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values.Remove("FORTUNA_AUTH_TOKEN_SECRET");

        var exception = Assert.Throws<InvalidOperationException>(
            () => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("FORTUNA_AUTH_TOKEN_SECRET", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenHeimdallClaims_WhenClaimsAreMapped_ThenSubjectRoleScopeAndPermissionsArePreserved()
    {
        var identity = new FortunaIdentity(
            Guid.NewGuid(),
            (int)HeimdallRoles.User,
            Guid.NewGuid(),
            ["fortuna.read", "fortuna.write"])
        {
            DisplayName = "Ada Lovelace"
        };
        var mapper = new FortunaIdentityMapper();

        var restored = mapper.FromClaims(mapper.ToClaims(identity));

        var restoredIdentity = Assert.IsType<FortunaIdentity>(restored);
        Assert.Equal(identity.SubjectId, restoredIdentity.SubjectId);
        Assert.Equal(identity.RoleId, restoredIdentity.RoleId);
        Assert.Equal(identity.ScopeId, restoredIdentity.ScopeId);
        Assert.Equal(identity.Permissions, restoredIdentity.Permissions);
        Assert.Equal(identity.DisplayName, restoredIdentity.DisplayName);
    }

    [UnitTheory]
    [InlineData("not-a-guid", "3")]
    [InlineData("00000000-0000-0000-0000-000000000001", "99")]
    public void GivenUnusableIdentityClaims_WhenClaimsAreMapped_ThenIdentityIsRejected(
        string subject, string role)
    {
        var claims = new Dictionary<string, string>
        {
            [FortunaIdentityMapper.SubjectClaim] = subject,
            [FortunaIdentityMapper.RoleClaim] = role
        };

        Assert.Null(new FortunaIdentityMapper().FromClaims(claims));
    }

    [UnitTheory]
    [InlineData(HeimdallRoles.SystemAdmin, FinancialRecordAccessResult.Forbidden)]
    [InlineData(HeimdallRoles.ScopeAdmin, FinancialRecordAccessResult.NotFound)]
    [InlineData(HeimdallRoles.User, FinancialRecordAccessResult.NotFound)]
    public void GivenCallerDoesNotOwnFinancialRecord_WhenAccessIsChecked_ThenNoDataIsDisclosed(
        HeimdallRoles role, FinancialRecordAccessResult expected)
    {
        var result = FinancialRecordAccess.Authorize(
            Guid.NewGuid(), (int)role, Guid.NewGuid());

        Assert.Equal(expected, result);
    }

    [UnitFact]
    public void GivenAccountOwnerOwnsFinancialRecord_WhenAccessIsChecked_ThenAccessIsAllowed()
    {
        var subject = Guid.NewGuid();

        var result = FinancialRecordAccess.Authorize(
            subject, (int)HeimdallRoles.User, subject);

        Assert.Equal(FinancialRecordAccessResult.Allowed, result);
    }

    [FunctionalFact]
    public async Task GivenNoToken_WhenProtectedEndpointIsRequested_ThenUnauthorizedIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/__tests/authentication/actor", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenMalformedToken_WhenProtectedEndpointIsRequested_ThenUnauthorizedHidesReason()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, "not-a-jwt");

        var response = await client.GetAsync("/__tests/authentication/actor", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("signature", body, StringComparison.OrdinalIgnoreCase);
    }

    [FunctionalTheory]
    [InlineData("wrong-secret-with-enough-entropy", Issuer, Audience, 3600)]
    [InlineData(Secret, "wrong-issuer", Audience, 3600)]
    [InlineData(Secret, Issuer, "wrong-audience", 3600)]
    [InlineData(Secret, Issuer, Audience, -60)]
    public async Task GivenUntrustedToken_WhenProtectedEndpointIsRequested_ThenUnauthorizedIsReturned(
        string secret, string issuer, string audience, double lifetimeSeconds)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, TokenFor(Guid.NewGuid(), HeimdallRoles.User, secret, issuer, audience, lifetimeSeconds));

        var response = await client.GetAsync("/__tests/authentication/actor", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenValidHeimdallToken_WhenProtectedEndpointIsRequested_ThenActorClaimsAreAvailable()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();
        var scope = Guid.NewGuid();
        Authorize(client, TokenFor(subject, HeimdallRoles.User, scopeId: scope,
            permissions: ["fortuna.read", "fortuna.write"]));

        var actor = await client.GetFromJsonAsync<ActorProbe>(
            "/__tests/authentication/actor", CancellationToken.None);

        Assert.NotNull(actor);
        Assert.Equal(subject, actor.SubjectId);
        Assert.Equal((int)HeimdallRoles.User, actor.RoleId);
        Assert.Equal(scope, actor.ScopeId);
        Assert.Equal(["fortuna.read", "fortuna.write"], actor.Permissions);
    }

    [FunctionalFact]
    public async Task GivenAnotherUsersRecord_WhenRequested_ThenNotFoundIsIndistinguishableFromMissing()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, TokenFor(Guid.NewGuid(), HeimdallRoles.User));

        var other = await client.GetAsync(
            $"/__tests/authentication/records/{Guid.NewGuid()}", CancellationToken.None);
        var missing = await client.GetAsync(
            $"/__tests/authentication/missing/{Guid.NewGuid()}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(missing.StatusCode, other.StatusCode);
        var otherProblem = await other.Content.ReadFromJsonAsync<ProblemDetails>();
        var missingProblem = await missing.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(missingProblem!.Status, otherProblem!.Status);
        Assert.Equal(missingProblem.Title, otherProblem.Title);
        Assert.Equal(missingProblem.Type, otherProblem.Type);
    }

    [FunctionalFact]
    public async Task GivenInstanceAdministrator_WhenFinancialRecordIsRequested_ThenForbiddenIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();
        Authorize(client, TokenFor(subject, HeimdallRoles.SystemAdmin));

        var response = await client.GetAsync(
            $"/__tests/authentication/records/{subject}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        foreach (var setting in ValidSettings())
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IUserProfileProvisioner>();
                services.AddSingleton<IUserProfileProvisioner, StubUserProfileProvisioner>();
                services.AddControllers().AddApplicationPart(typeof(AuthenticationProbeController).Assembly);
            });
        });
    }

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string TokenFor(
        Guid subject,
        HeimdallRoles role,
        string secret = Secret,
        string issuer = Issuer,
        string audience = Audience,
        double lifetimeSeconds = 3600,
        Guid? scopeId = null,
        IReadOnlyCollection<string>? permissions = null)
    {
        var identity = new FortunaIdentity(subject, (int)role, scopeId, permissions ?? [])
        {
            DisplayName = "Test User"
        };

        if (lifetimeSeconds < 0)
        {
            var now = DateTime.UtcNow;
            var claims = new FortunaIdentityMapper().ToClaims(identity)
                .Select(claim => new Claim(claim.Key, claim.Value));
            var descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Issuer = issuer,
                Audience = audience,
                NotBefore = now.AddMinutes(-2),
                IssuedAt = now.AddMinutes(-2),
                Expires = now.AddMinutes(-1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret)),
                    SecurityAlgorithms.HmacSha256)
            };

            return new JwtSecurityTokenHandler().WriteToken(
                new JwtSecurityTokenHandler().CreateToken(descriptor));
        }

        var configuration = new JwtConfiguration(
            lifetimeSeconds,
            issuer,
            audience,
            secret,
            new FortunaIdentityMapper().ToClaims(identity));

        return new JwtHandler().CreateToken(configuration);
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR"
    };

    private sealed class StubUserProfileProvisioner : IUserProfileProvisioner
    {
        public Task<UserProfileSnapshot> GetOrCreateAsync(
            Guid externalSubject,
            string displayName,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;

            return Task.FromResult(new UserProfileSnapshot(
                Guid.NewGuid(),
                externalSubject,
                displayName,
                "BRL",
                false,
                now,
                now));
        }
    }

    public sealed record ActorProbe(
        Guid SubjectId,
        int RoleId,
        Guid? ScopeId,
        IReadOnlyCollection<string> Permissions);
}

[ApiController]
[Route("__tests/authentication")]
public sealed class AuthenticationProbeController(IRequestActorAccessor actorAccessor) : ControllerBase
{
    [HttpGet("actor")]
    public ActionResult<AuthenticationTests.ActorProbe> Actor()
    {
        var actor = actorAccessor.Actor!;

        return Ok(new AuthenticationTests.ActorProbe(
            actor.SubjectId, actor.RoleId, actor.ScopeId, actor.Permissions));
    }

    [HttpGet("records/{ownerId:guid}")]
    public IActionResult Record(Guid ownerId)
    {
        var actor = actorAccessor.Actor!;

        return FinancialRecordAccess.Authorize(actor.SubjectId, actor.RoleId, ownerId) switch
        {
            FinancialRecordAccessResult.Allowed => Ok(),
            FinancialRecordAccessResult.Forbidden => Forbid(),
            _ => NotFound()
        };
    }

    [HttpGet("missing/{id:guid}")]
    public IActionResult Missing(Guid id) => NotFound();
}
