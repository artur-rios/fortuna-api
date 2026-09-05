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
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new ArgumentException(
                "A tag name between 1 and 200 characters is required.",
                nameof(name));
        }

        UserId = user.Id;
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
}
