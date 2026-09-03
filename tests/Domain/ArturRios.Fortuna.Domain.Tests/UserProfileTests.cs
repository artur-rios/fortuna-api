using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class UserProfileTests
{
    [UnitFact]
    public void GivenValidHeimdallIdentity_WhenProfileIsCreated_ThenOnlyProfileDataIsStored()
    {
        var subject = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var profile = new UserProfile(
            subject,
            "Ada Lovelace",
            new Currency("BRL", "Brazilian Real", 2),
            now);

        Assert.NotEqual(Guid.Empty, profile.PublicId);
        Assert.Equal(subject.ToString("D"), profile.ExternalSubject);
        Assert.Equal("Ada Lovelace", profile.DisplayName);
        Assert.Equal("BRL", profile.DisplayCurrency.Code);
        Assert.Equal(now, profile.CreatedAt);
        Assert.Equal(now, profile.UpdatedAt);
        Assert.DoesNotContain(
            typeof(UserProfile).GetProperties(),
            property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenDisplayNameMissing_WhenProfileIsCreated_ThenItIsRejected(string displayName)
    {
        Assert.Throws<ArgumentException>(() => new UserProfile(
            Guid.NewGuid(),
            displayName,
            new Currency("BRL", "Brazilian Real", 2),
            DateTimeOffset.UtcNow));
    }
}
