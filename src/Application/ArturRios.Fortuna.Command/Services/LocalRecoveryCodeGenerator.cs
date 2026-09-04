using System.Security.Cryptography;
using System.Text;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Random;

namespace ArturRios.Fortuna.Command.Services;

public sealed class LocalRecoveryCodeGenerator : ILocalRecoveryCodeGenerator
{
    private const int SegmentLength = 4;

    public IReadOnlyCollection<GeneratedRecoveryCode> Generate(int count)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        while (values.Count < count)
        {
            var raw = CustomRandom.Text(new RandomStringOptions
            {
                Length = SegmentLength * 2,
                IncludeDigits = true,
                IncludeUppercase = true,
                IncludeLowercase = false,
                IncludeSpecialCharacters = false
            });
            values.Add($"{raw[..SegmentLength]}-{raw[SegmentLength..]}");
        }

        return values
            .Select(value => new GeneratedRecoveryCode(
                value,
                SHA256.HashData(Encoding.UTF8.GetBytes(value))))
            .ToArray();
    }
}
