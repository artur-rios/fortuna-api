namespace ArturRios.Fortuna.Shared.Security;

/// <summary>
/// Exposes the current caller without making application handlers depend on ASP.NET Core.
/// </summary>
public interface IRequestActorAccessor
{
    RequestActor? Actor { get; }
}
