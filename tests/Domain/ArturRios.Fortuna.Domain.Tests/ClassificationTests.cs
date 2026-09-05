using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class ClassificationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenOwnedNames_WhenCreated_ThenClassificationNamesAreNormalized()
    {
        var user = User();

        var category = new Category(user, " Dining ", Now);
        var tag = new Tag(user, " Food ", Now);
        var counterparty = new Counterparty(user, " Corner Cafe ", Now);

        Assert.Equal("Dining", category.Name);
        Assert.Equal("DINING", category.NormalizedName);
        Assert.Equal("Food", tag.Name);
        Assert.Equal("FOOD", tag.NormalizedName);
        Assert.Equal("Corner Cafe", counterparty.Name);
        Assert.Equal("CORNER CAFE", counterparty.NormalizedName);
    }

    [UnitFact]
    public void GivenParentFromAnotherOwner_WhenCategoryCreated_ThenItIsRejected()
    {
        var user = User();
        var foreignParent = new Category(User(), "Parent", Now);

        var exception = Assert.Throws<ArgumentException>(() =>
            new Category(user, "Child", Now, foreignParent));

        Assert.Equal("parent", exception.ParamName);
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenMissingName_WhenClassificationCreated_ThenItIsRejected(string name)
    {
        var user = User();

        Assert.Throws<ArgumentException>(() => new Category(user, name, Now));
        Assert.Throws<ArgumentException>(() => new Tag(user, name, Now));
        Assert.Throws<ArgumentException>(() => new Counterparty(user, name, Now));
    }

    private static UserProfile User() => new(
        Guid.NewGuid(),
        "Owner",
        new Currency("BRL", "Brazilian real", 2),
        Now);
}
