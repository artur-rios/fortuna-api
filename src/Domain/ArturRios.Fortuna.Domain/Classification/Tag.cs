using ArturRios.Fortuna.Domain.Guards;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Classification;

public sealed class Tag : RecordLifecycleEntity
{
    private Tag()
    {
    }

    public Tag(UserProfile user, string name, DateTimeOffset createdAt) : base(createdAt)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        name = BoundedText.Required(
            name,
            200,
            nameof(name),
            "A tag name between 1 and 200 characters is required.");

        UserId = user.Id;
        Name = name;
        NormalizedName = Name.ToUpperInvariant();
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;

    public void Rename(string name, DateTimeOffset updatedAt)
    {
        EnsureNotDeleted();
        name = BoundedText.Required(
            name,
            200,
            nameof(name),
            "A tag name between 1 and 200 characters is required.");

        Name = name;
        NormalizedName = Name.ToUpperInvariant();
        MarkUpdated(updatedAt);
    }
}
