using System.Security.Cryptography;
using System.Text;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// Which keys may verify a token, chosen by the issuer it names, and the single algorithm each key verifies.
/// </summary>
public sealed class FortunaSigningKeysTests
{
    private const string LocalIssuer = "fortuna-local";

    /// <summary>A shared secret long enough for IdentityModel to compute HS384 and HS512 over it.</summary>
    private const string LongSharedSecret = "fortuna-tests-signing-key-long-enough-for-hs512-with-plenty-of-entropy";

    private static async Task<(FortunaSigningKeys Keys, JwtKey HeimdallKey, string HeimdallPem)> CreateAsync(
        bool sharedSecret = true, string? sharedSecretValue = null)
    {
        var heimdallPem = JwksTestSupport.NewPem();
        var heimdallKey = JwtKey.FromEcPrivateKeyPem(heimdallPem);
        var settings = JwksTestSupport.Settings(sharedSecret);

        if (sharedSecretValue is not null)
        {
            settings["FORTUNA_AUTH_TOKEN_SECRET"] = sharedSecretValue;
        }

        var keys = await JwksTestSupport.SigningKeysAsync(FortunaOptions.From(settings.GetValueOrDefault), heimdallKey);

        return (keys, heimdallKey, heimdallPem);
    }

    private static SecurityToken Naming(string issuer) =>
        new JsonWebToken("{\"alg\":\"none\"}", $"{{\"iss\":\"{issuer}\"}}");

    /// <summary>Validates a token as Fortuna's bearer scheme does: keys by issuer, one algorithm per key.</summary>
    private static Task<TokenValidationResult> ValidateAsync(FortunaSigningKeys keys, string token) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, JwksTestSupport.ValidationParameters(keys));

    /// <summary>A token as Fortuna's own library writes it: HS256 with no kid, or ES256 under the key's kid.</summary>
    private static string Issue(string issuer, string secret, JwtKey? ecKey = null) =>
        new JwtHandler().CreateToken(new JwtConfiguration(
            3600, issuer, JwksTestSupport.Audience, ecKey is null ? secret : string.Empty,
            new Dictionary<string, string> { ["id"] = "42" })
        {
            Algorithm = ecKey is null ? JwtAlgorithms.HS256 : JwtAlgorithms.ES256,
            Keys = ecKey is null ? [] : [ecKey],
            SigningKeyId = ecKey?.Id
        });

    /// <summary>A token signed however an attacker likes, under Heimdall's issuer.</summary>
    private static string Forge(SigningCredentials credentials) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = JwksTestSupport.Issuer,
            Audience = JwksTestSupport.Audience,
            Claims = new Dictionary<string, object> { ["id"] = "42" },
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = credentials
        });

    /// <summary>Bytes of Heimdall's public key anyone can have: its SubjectPublicKeyInfo as PEM text, or as DER.</summary>
    private static byte[] PublicKeyBytes(string heimdallPem, string form)
    {
        var publicPem = JwksTestSupport.PublicPemOf(heimdallPem);

        if (form == "pem")
        {
            return Encoding.ASCII.GetBytes(publicPem);
        }

        using var key = ECDsa.Create();
        key.ImportFromPem(publicPem);

        return key.ExportSubjectPublicKeyInfo();
    }

    [UnitFact]
    public async Task GivenHeimdallsIssuerAndAPublishedKid_WhenResolving_ThenThatEcKeyAlone()
    {
        var (keys, heimdallKey, _) = await CreateAsync();

        var resolved = Assert.Single(keys.Resolve(Naming(JwksTestSupport.Issuer), heimdallKey.Id));

        Assert.IsType<ECDsaSecurityKey>(resolved);
        Assert.Equal(heimdallKey.Id, resolved.KeyId);
    }

    [UnitFact]
    public async Task GivenHeimdallsIssuerAndAnUnknownKid_WhenResolving_ThenNoKey()
    {
        var (keys, _, _) = await CreateAsync();

        Assert.Empty(keys.Resolve(Naming(JwksTestSupport.Issuer), "unknown"));
    }

    [UnitFact]
    public async Task GivenHeimdallsIssuerWithoutAKidDuringTheOverlap_WhenResolving_ThenTheSharedSecret()
    {
        var (keys, _, _) = await CreateAsync();

        var resolved = Assert.IsType<SymmetricSecurityKey>(Assert.Single(keys.Resolve(Naming(JwksTestSupport.Issuer), null)));

        Assert.True(keys.AcceptsLegacyHeimdallTokens);
        Assert.Equal(Encoding.ASCII.GetBytes(JwksTestSupport.SharedSecret), resolved.Key);
    }

    [UnitFact]
    public async Task GivenHeimdallsIssuerWithoutAKidAfterTheOverlap_WhenResolving_ThenNoKey()
    {
        var (keys, _, _) = await CreateAsync(sharedSecret: false);

        Assert.False(keys.AcceptsLegacyHeimdallTokens);
        Assert.Empty(keys.Resolve(Naming(JwksTestSupport.Issuer), null));
    }

    [UnitTheory]
    [InlineData(null)]
    [InlineData("any-kid")]
    public async Task GivenTheLocalIssuer_WhenResolving_ThenFortunasOwnSecretAlone(string? keyId)
    {
        var (keys, _, _) = await CreateAsync();

        var resolved = Assert.IsType<SymmetricSecurityKey>(Assert.Single(keys.Resolve(Naming(LocalIssuer), keyId)));

        Assert.Equal(Encoding.ASCII.GetBytes(JwksTestSupport.LocalSecret), resolved.Key);
    }

    [UnitTheory]
    [InlineData("another-service")]
    [InlineData("")]
    public async Task GivenAnyOtherIssuer_WhenResolving_ThenNoKey(string issuer)
    {
        var (keys, heimdallKey, _) = await CreateAsync();

        Assert.Empty(keys.Resolve(Naming(issuer), heimdallKey.Id));
        Assert.Empty(keys.Resolve(Naming(issuer), null));
    }

    [UnitTheory]
    [InlineData(SecurityAlgorithms.EcdsaSha256, "ec", true)]
    [InlineData(SecurityAlgorithms.HmacSha256, "ec", false)]
    [InlineData(SecurityAlgorithms.EcdsaSha384, "ec", false)]
    [InlineData("none", "ec", false)]
    [InlineData(SecurityAlgorithms.HmacSha256, "secret", true)]
    [InlineData(SecurityAlgorithms.EcdsaSha256, "secret", false)]
    [InlineData(SecurityAlgorithms.HmacSha512, "secret", false)]
    [InlineData(SecurityAlgorithms.RsaSha256, "rsa", false)]
    public void GivenAnAlgorithmAndAKey_WhenChecked_ThenOnlyTheKeysOwnAlgorithmIsAllowed(
        string algorithm, string keyKind, bool allowed)
    {
        SecurityKey key = keyKind switch
        {
            "ec" => JwksTestSupport.NewKey().ToSecurityKey(),
            "secret" => new SymmetricSecurityKey(Encoding.ASCII.GetBytes(JwksTestSupport.SharedSecret)),
            _ => new RsaSecurityKey(RSA.Create(2048))
        };

        Assert.Equal(allowed, FortunaSigningKeys.IsAllowed(algorithm, key));
    }

    [UnitFact]
    public async Task GivenAnEs256TokenSignedByHeimdall_WhenValidated_ThenItIsAccepted()
    {
        var (keys, heimdallKey, _) = await CreateAsync();

        var result = await ValidateAsync(keys, Issue(JwksTestSupport.Issuer, string.Empty, heimdallKey));

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [UnitFact]
    public async Task GivenAnHs256HeimdallTokenDuringTheOverlap_WhenValidated_ThenItIsAccepted()
    {
        var (keys, _, _) = await CreateAsync();

        var result = await ValidateAsync(keys, Issue(JwksTestSupport.Issuer, JwksTestSupport.SharedSecret));

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [UnitFact]
    public async Task GivenAnHs256HeimdallTokenAfterTheOverlap_WhenValidated_ThenItIsRefused()
    {
        var (keys, _, _) = await CreateAsync(sharedSecret: false);

        var result = await ValidateAsync(keys, Issue(JwksTestSupport.Issuer, JwksTestSupport.SharedSecret));

        Assert.False(result.IsValid);
    }

    [UnitFact]
    public async Task GivenALocalTokenSignedWithTheLocalSecret_WhenValidated_ThenItIsAccepted()
    {
        var (keys, _, _) = await CreateAsync(sharedSecret: false);

        var result = await ValidateAsync(keys, Issue(LocalIssuer, JwksTestSupport.LocalSecret));

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [UnitFact]
    public async Task GivenHeimdallsIssuerSignedWithTheLocalSecret_WhenValidated_ThenItIsRefused()
    {
        var (keys, _, _) = await CreateAsync();

        var result = await ValidateAsync(keys, Issue(JwksTestSupport.Issuer, JwksTestSupport.LocalSecret));

        Assert.False(result.IsValid);
    }

    [UnitFact]
    public async Task GivenTheLocalIssuerSignedWithTheSharedSecret_WhenValidated_ThenItIsRefused()
    {
        var (keys, _, _) = await CreateAsync();

        var result = await ValidateAsync(keys, Issue(LocalIssuer, JwksTestSupport.SharedSecret));

        Assert.False(result.IsValid);
    }

    /// <summary>
    /// The shared secret is HS256's alone: without the per-key algorithm check, an HS384 or HS512 token signed
    /// with it would validate, because IdentityModel accepts any HMAC algorithm over a symmetric key long
    /// enough for it.
    /// </summary>
    [UnitTheory]
    [InlineData(SecurityAlgorithms.HmacSha384)]
    [InlineData(SecurityAlgorithms.HmacSha512)]
    public async Task GivenAnotherHmacAlgorithmOverTheSharedSecret_WhenValidated_ThenItIsRefused(string algorithm)
    {
        var (keys, _, _) = await CreateAsync(sharedSecretValue: LongSharedSecret);
        var secret = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(LongSharedSecret));

        var result = await ValidateAsync(keys, Forge(new SigningCredentials(secret, algorithm)));

        Assert.False(result.IsValid);
    }

    /// <summary>
    /// The algorithm-confusion attack: an HS256 token whose HMAC key is Heimdall's public key, naming
    /// Heimdall's kid. The kid resolves to the EC key alone, never to a secret made of its bytes, and an EC key
    /// verifies ES256 only. IdentityModel has no HMAC over an EC key either; the assertion on
    /// <see cref="FortunaSigningKeys.IsAllowed" /> is what pins Fortuna's own refusal, so that it does not
    /// rest on a library default.
    /// </summary>
    [UnitTheory]
    [InlineData("pem")]
    [InlineData("der")]
    public async Task GivenAnHs256TokenKeyedWithHeimdallsPublicKey_WhenValidated_ThenItIsRefused(string form)
    {
        var (keys, heimdallKey, heimdallPem) = await CreateAsync();
        var confused = new SymmetricSecurityKey(PublicKeyBytes(heimdallPem, form)) { KeyId = heimdallKey.Id };
        var token = Forge(new SigningCredentials(confused, SecurityAlgorithms.HmacSha256));
        var read = new JsonWebToken(token);

        var result = await ValidateAsync(keys, token);
        var resolved = keys.Resolve(read, read.Kid).ToList();

        Assert.False(result.IsValid);
        Assert.Equal(heimdallKey.Id, read.Kid);
        Assert.IsType<ECDsaSecurityKey>(Assert.Single(resolved));
        Assert.All(resolved, key => Assert.False(FortunaSigningKeys.IsAllowed(read.Alg, key)));
    }

    /// <summary>The same attack without a kid: it meets the shared secret alone, which its HMAC key is not.</summary>
    [UnitTheory]
    [InlineData("pem")]
    [InlineData("der")]
    public async Task GivenAnHs256TokenKeyedWithHeimdallsPublicKeyWithoutAKid_WhenValidated_ThenItIsRefused(string form)
    {
        var (keys, _, heimdallPem) = await CreateAsync();
        var confused = new SymmetricSecurityKey(PublicKeyBytes(heimdallPem, form));
        var token = Forge(new SigningCredentials(confused, SecurityAlgorithms.HmacSha256));
        var read = new JsonWebToken(token);

        var result = await ValidateAsync(keys, token);
        var resolved = Assert.IsType<SymmetricSecurityKey>(Assert.Single(keys.Resolve(read, read.Kid)));

        Assert.False(result.IsValid);
        Assert.Equal(Encoding.ASCII.GetBytes(JwksTestSupport.SharedSecret), resolved.Key);
    }
}
