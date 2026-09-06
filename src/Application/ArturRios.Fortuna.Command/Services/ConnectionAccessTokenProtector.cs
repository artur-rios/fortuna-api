using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace ArturRios.Fortuna.Command.Services;

public sealed class ConnectionAccessTokenProtector : IConnectionAccessTokenProtector
{
    private const string Purpose = "ArturRios.Fortuna.Ingestion.ConnectionAccessToken.v1";
    private readonly IDataProtector _protector;

    public ConnectionAccessTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Protect(string accessToken) =>
        _protector.Protect(Encoding.UTF8.GetBytes(accessToken));

    public string Unprotect(byte[] protectedAccessToken) =>
        Encoding.UTF8.GetString(_protector.Unprotect(protectedAccessToken));
}
