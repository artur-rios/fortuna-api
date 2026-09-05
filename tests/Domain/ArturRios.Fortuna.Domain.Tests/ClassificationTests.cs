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

    [UnitFact]
    public void GivenNewDetails_WhenCategoryUpdated_ThenNameParentAndTimestampChange()
    {
        var user = User();
        var originalParent = new Category(user, "Original", Now);
        var newParent = new Category(user, "New", Now);
        var category = new Category(user, "Before", Now, originalParent);
        var updatedAt = Now.AddHours(1);

        category.UpdateDetails("  After  ", newParent, updatedAt);

        Assert.Equal("After", category.Name);
        Assert.Equal("AFTER", category.NormalizedName);
        Assert.Equal(newParent, category.Parent);
        Assert.Equal(newParent.Id, category.ParentId);
        Assert.Equal(Now, category.CreatedAt);
        Assert.Equal(updatedAt, category.UpdatedAt);
    }

    [UnitFact]
    public void GivenNullParent_WhenCategoryUpdated_ThenItMovesToRoot()
    {
        var user = User();
        var category = new Category(user, "Child", Now, new Category(user, "Root", Now));

        category.UpdateDetails("Child", null, Now.AddMinutes(1));

        Assert.Null(category.Parent);
        Assert.Null(category.ParentId);
    }

    [UnitFact]
    public void GivenInvalidDetails_WhenCategoryUpdated_ThenTheyAreRejected()
    {
        var user = User();
        var category = new Category(user, "Category", Now);

        Assert.Throws<ArgumentException>(() =>
            category.UpdateDetails(" ", null, Now));
        Assert.Throws<ArgumentException>(() =>
            category.UpdateDetails(new string('n', 201), null, Now));
        Assert.Throws<ArgumentException>(() =>
            category.UpdateDetails("Category", new Category(User(), "Foreign", Now), Now));
        Assert.Throws<ArgumentException>(() =>
            category.UpdateDetails("Category", category, Now));
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
