using ArturRios.Fortuna.Command.Services;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.DataProtection;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class ConnectionAccessTokenProtectorTests
{
    [UnitFact]
    public void GivenAccessToken_WhenProtected_ThenCipherRoundTripsWithoutPlaintextStorage()
    {
        const string accessToken = "pluggy-access-token";
        var protector = new ConnectionAccessTokenProtector(
            new EphemeralDataProtectionProvider());

        var cipher = protector.Protect(accessToken);
        var roundTrip = protector.Unprotect(cipher);

        Assert.NotEqual(accessToken, System.Text.Encoding.UTF8.GetString(cipher));
        Assert.Equal(accessToken, roundTrip);
    }
}
