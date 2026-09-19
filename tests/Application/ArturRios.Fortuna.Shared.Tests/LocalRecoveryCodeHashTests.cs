using System.Security.Cryptography;
using System.Text;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Shared.Tests;

public sealed class LocalRecoveryCodeHashTests
{
    private const string Code = "ABCD-1234";

    [UnitFact]
    public void GivenSameCode_WhenHashedTwice_ThenDigestsAreSaltedAndBothVerify()
    {
        var first = LocalRecoveryCodeHash.Compute(Code);
        var second = LocalRecoveryCodeHash.Compute(Code);

        Assert.NotEqual(first, second);
        Assert.True(LocalRecoveryCodeHash.Matches(Code, first));
        Assert.True(LocalRecoveryCodeHash.Matches(Code, second));
    }

    [UnitFact]
    public void GivenStoredDigest_WhenDifferentCodeIsOffered_ThenItDoesNotMatch()
    {
        var stored = LocalRecoveryCodeHash.Compute(Code);

        Assert.False(LocalRecoveryCodeHash.Matches("ABCD-1235", stored));
    }

    [UnitTheory]
    [InlineData("abcd-1234")]
    [InlineData("  ABCD-1234\t")]
    [InlineData(" aBcD-1234 ")]
    public void GivenCodeTypedWithOtherCaseOrWhitespace_WhenVerified_ThenItMatches(string typed)
    {
        var stored = LocalRecoveryCodeHash.Compute(Code);

        Assert.True(LocalRecoveryCodeHash.Matches(typed, stored));
    }

    [UnitFact]
    public void GivenLegacySha256Digest_WhenVerified_ThenItStillMatchesNormalizedInput()
    {
        var legacy = SHA256.HashData(Encoding.UTF8.GetBytes(Code));

        Assert.True(LocalRecoveryCodeHash.Matches(Code, legacy));
        Assert.True(LocalRecoveryCodeHash.Matches(" abcd-1234 ", legacy));
        Assert.False(LocalRecoveryCodeHash.Matches("ABCD-9999", legacy));
    }

    [UnitTheory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { 9, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 })]
    public void GivenUnknownDigestFormat_WhenVerified_ThenItDoesNotMatch(byte[] stored)
    {
        Assert.False(LocalRecoveryCodeHash.Matches(Code, stored));
    }

    [UnitFact]
    public void GivenCurrentDigest_WhenInspected_ThenItIsNotAPlainSha256OfTheCode()
    {
        var stored = LocalRecoveryCodeHash.Compute(Code);

        Assert.NotEqual(32, stored.Length);
        Assert.Equal(1, stored[0]);
    }
}
