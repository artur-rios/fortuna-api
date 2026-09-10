using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Auditing;

/// <summary>Revocable mapping from a user to the opaque subject used by audit entries.</summary>
public sealed class AuditSubject
{
    private AuditSubject()
    {
    }

    public AuditSubject(UserProfile user)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        UserId = user.Id;
        SubjectReference = Guid.NewGuid();
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public Guid SubjectReference { get; private set; }
}
