using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Classification;

public sealed class Category : RecordLifecycleEntity
{
    private Category()
    {
    }

    public Category(
        UserProfile user,
        string name,
        DateTimeOffset createdAt,
        Category? parent = null) : base(createdAt)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new ArgumentException(
                "A category name between 1 and 200 characters is required.",
                nameof(name));
        }

        if (parent is not null && parent.User.PublicId != user.PublicId)
        {
            throw new ArgumentException(
                "A category and its parent must have the same owner.",
                nameof(parent));
        }

        UserId = user.Id;
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        Parent = parent;
        ParentId = parent?.Id;
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public long? ParentId { get; private set; }
    public Category? Parent { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;

    public void UpdateDetails(
        string name,
        Category? parent,
        DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new ArgumentException(
                "A category name between 1 and 200 characters is required.",
                nameof(name));
        }

        if (parent is not null && parent.User.PublicId != User.PublicId)
        {
            throw new ArgumentException(
                "A category and its parent must have the same owner.",
                nameof(parent));
        }

        if (parent is not null &&
            (ReferenceEquals(parent, this) || parent.PublicId == PublicId))
        {
            throw new ArgumentException(
                "A category cannot be its own parent.",
                nameof(parent));
        }

        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        Parent = parent;
        ParentId = parent?.Id;
        MarkUpdated(updatedAt);
    }
}
