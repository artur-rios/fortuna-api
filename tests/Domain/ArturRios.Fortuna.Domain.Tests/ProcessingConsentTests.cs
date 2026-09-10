using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class ProcessingConsentTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenExplicitDecision_WhenGrantedAgain_ThenVersionAndDecisionTimeAreReplaced()
    {
        var consent = new ProcessingConsent(
            Profile(), ProcessingConsentPurpose.ExternalDataProcessing, "1.0", Now);

        consent.Grant(" 2.0 ", Now.AddDays(1));

        Assert.NotEqual(Guid.Empty, consent.PublicId);
        Assert.Equal("2.0", consent.Version);
        Assert.Equal(Now.AddDays(1), consent.GrantedAt);
        Assert.Equal(Now.AddDays(1), consent.UpdatedAt);
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenMissingVersion_WhenConsentIsCreated_ThenItIsRejected(string version)
    {
        Assert.Throws<ArgumentException>(() => new ProcessingConsent(
            Profile(), ProcessingConsentPurpose.ExternalDataProcessing, version, Now));
    }

    [UnitFact]
    public void GivenUnknownPurpose_WhenConsentIsCreated_ThenItIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingConsent(
            Profile(), (ProcessingConsentPurpose)999, "1.0", Now));
    }

    private static UserProfile Profile() => new(
        Guid.NewGuid(), "Owner", new Currency("BRL", "Brazilian Real", 2), Now);
}
