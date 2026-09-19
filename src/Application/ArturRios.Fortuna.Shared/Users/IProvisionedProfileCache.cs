namespace ArturRios.Fortuna.Shared.Users;

/// <summary>
///     Remembers, for a short while, which external subjects already have a Fortuna profile, so
///     profile provisioning does not reach the database on every authenticated request.
/// </summary>
public interface IProvisionedProfileCache
{
    bool IsProvisioned(Guid externalSubject);

    void MarkProvisioned(Guid externalSubject);

    /// <summary>
    ///     Forgets a subject whose profile was erased. For the cache lifetime the subject is not
    ///     marked provisioned again, so a request racing the erasure cannot leave a stale entry.
    /// </summary>
    void Forget(Guid externalSubject);
}
