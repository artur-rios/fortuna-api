using ArturRios.Fortuna.Domain.Guards;
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
        name = BoundedText.Required(
            name,
            200,
            nameof(name),
            "A category name between 1 and 200 characters is required.");

        if (parent is not null && parent.User.PublicId != user.PublicId)
        {
            throw new ArgumentException(
                "A category and its parent must have the same owner.",
                nameof(parent));
        }

        if (parent?.IsDeleted == true)
        {
            throw new ArgumentException("A deleted category cannot be a parent.", nameof(parent));
        }

        UserId = user.Id;
        Name = name;
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
        EnsureNotDeleted();
        name = BoundedText.Required(
            name,
            200,
            nameof(name),
            "A category name between 1 and 200 characters is required.");

        if (parent is not null && parent.User.PublicId != User.PublicId)
        {
            throw new ArgumentException(
                "A category and its parent must have the same owner.",
                nameof(parent));
        }

        if (parent?.IsDeleted == true)
        {
            throw new ArgumentException("A deleted category cannot be a parent.", nameof(parent));
        }

        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            // Walks the ancestors that are loaded; the store checks the persisted hierarchy.
            if (ReferenceEquals(ancestor, this) || ancestor.PublicId == PublicId)
            {
                throw new ArgumentException(
                    "A category cannot be its own parent or ancestor.",
                    nameof(parent));
            }
        }

        Name = name;
        NormalizedName = Name.ToUpperInvariant();
        Parent = parent;
        ParentId = parent?.Id;
        MarkUpdated(updatedAt);
    }
}
