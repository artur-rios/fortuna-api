using System.Security.Cryptography;
using System.Text;
using ArturRios.Fortuna.Command.Services;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class LocalRecoveryCodeGeneratorTests
{
    [UnitFact]
    public void GivenRequestedCount_WhenGeneratingRecoveryCodes_ThenValuesAreUniqueAndHashesMatch()
    {
        var codes = new LocalRecoveryCodeGenerator().Generate(10);

        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Select(code => code.Value).Distinct().Count());
        Assert.All(codes, code =>
        {
            Assert.Matches("^[A-Z0-9]{4}-[A-Z0-9]{4}$", code.Value);
            Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(code.Value)), code.Hash);
        });
    }
}
