namespace ArturRios.Fortuna.Shared.Users;

public interface ILocalRecoveryCodeGenerator
{
    IReadOnlyCollection<GeneratedRecoveryCode> Generate(int count);
}

public sealed record GeneratedRecoveryCode(string Value, byte[] Hash);
