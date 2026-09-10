using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Attachments;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArturRios.Fortuna.Data.Exports;

/// <summary>Builds the complete, versioned portability archive from owner-scoped projections.</summary>
public sealed class EfPersonalDataArchiveBuilder(
    AppDbContext context,
    IAttachmentStore objects) : IPersonalDataArchiveBuilder
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<PersonalDataArchive> BuildAsync(
        Guid userId,
        DateTimeOffset generatedAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var internalUserId = await context.UserProfiles
            .Where(item => item.PublicId == userId)
            .Select(item => item.Id)
            .SingleAsync(cancellationToken);
        var subjectReference = await context.AuditSubjects
            .Where(item => item.UserId == internalUserId)
            .Select(item => (Guid?)item.SubjectReference)
            .SingleOrDefaultAsync(cancellationToken);
        var parts = await LoadPartsAsync(internalUserId, subjectReference, cancellationToken);
        var currencies = await context.Currencies.AsNoTracking()
            .ToDictionaryAsync(item => item.Id, item => item.Code, cancellationToken);
        var publicIds = PublicIdLookup(parts);
        var attachmentPaths = parts
            .Single(part => part.Descriptor.EntityType == typeof(Attachment))
            .Records
            .Cast<Attachment>()
            .ToDictionary(
                item => item.Id,
                item => $"attachments/{item.PublicId:N}/{SafeFileName(item.FileName)}");

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestParts = new List<object>();
            var recordCount = 0;
            foreach (var part in parts)
            {
                var records = part.Records
                    .Select(item => PortableRecord(
                        part.Descriptor.EntityType,
                        item,
                        publicIds,
                        currencies,
                        attachmentPaths))
                    .ToArray();
                recordCount += records.Length;
                var dataPath = $"data/{part.Descriptor.Name}.json";
                var schemaPath = $"schemas/{part.Descriptor.Name}.schema.json";
                await WriteJsonAsync(
                    archive,
                    dataPath,
                    new { schemaVersion = SchemaVersion, records },
                    cancellationToken);
                await WriteJsonAsync(
                    archive,
                    schemaPath,
                    Schema(part.Descriptor.EntityType),
                    cancellationToken);
                manifestParts.Add(new
                {
                    name = part.Descriptor.Name,
                    path = dataPath,
                    schema = schemaPath,
                    count = records.Length
                });
            }

            foreach (var attachment in parts
                         .Single(part => part.Descriptor.EntityType == typeof(Attachment))
                         .Records
                         .Cast<Attachment>())
            {
                await using var source = await objects.OpenReadAsync(
                    attachment.StorageKey,
                    cancellationToken);
                var entry = archive.CreateEntry(
                    attachmentPaths[attachment.Id],
                    CompressionLevel.Optimal);
                await using var target = entry.Open();
                await source.CopyToAsync(target, cancellationToken);
            }

            await WriteJsonAsync(archive, "manifest.json", new
            {
                schemaVersion = SchemaVersion,
                archiveType = "fortuna-personal-data",
                generatedAt,
                expiresAt,
                parts = manifestParts,
                attachments = attachmentPaths.Values.Order(StringComparer.Ordinal).ToArray()
            }, cancellationToken);
        }

        return new PersonalDataArchive(
            output.ToArray(),
            parts.Sum(part => part.Records.Count),
            parts.Select(part => part.Descriptor.Name).ToArray());
    }

    private async Task<IReadOnlyList<LoadedPart>> LoadPartsAsync(
        long userId,
        Guid? subjectReference,
        CancellationToken cancellationToken) =>
    [
        new(PersonalDataArchiveCoverage.Profile,
            await BoxAsync(context.UserProfiles.AsNoTracking()
                .Where(item => item.Id == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.FinancialAccounts,
            await BoxAsync(context.FinancialAccounts.AsNoTracking()
                .Where(item => item.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.CreditCards,
            await BoxAsync(context.CreditCards.AsNoTracking()
                .Where(item => item.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.CreditCardStatements,
            await BoxAsync(context.CreditCardStatements.AsNoTracking()
                .Where(item => item.CreditCard.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.Investments,
            await BoxAsync(context.Investments.AsNoTracking()
                .Where(item => item.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.InvestmentMovements,
            await BoxAsync(context.InvestmentMovements.AsNoTracking()
                .Where(item => item.Investment.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.InvestmentValuations,
            await BoxAsync(context.InvestmentValuations.AsNoTracking()
                .Where(item => item.Investment.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.Transactions,
            await BoxAsync(context.FinancialTransactions.AsNoTracking()
                .Include(item => item.Tags)
                .Where(item => item.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.Transfers,
            await BoxAsync(context.Transfers.AsNoTracking()
                .Where(item => item.OutboundTransaction.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.InstallmentPlans,
            await BoxAsync(context.InstallmentPlans.AsNoTracking()
                .Where(item => item.CreditCard.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.RecurringTransactions,
            await BoxAsync(context.RecurringTransactions.AsNoTracking()
                .Where(item => item.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.Categories,
            await BoxAsync(context.Categories.AsNoTracking()
                .Where(item => item.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.Tags,
            await BoxAsync(context.Tags.AsNoTracking()
                .Where(item => item.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.Counterparties,
            await BoxAsync(context.Counterparties.AsNoTracking()
                .Where(item => item.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.Budgets,
            await BoxAsync(context.Budgets.AsNoTracking()
                .Include(item => item.Categories)
                .Where(item => item.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.Goals,
            await BoxAsync(context.Goals.AsNoTracking()
                .Include(item => item.Accounts)
                .Include(item => item.Investments)
                .Where(item => item.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.Connections,
            await BoxAsync(context.Connections.AsNoTracking()
                .Where(item => item.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.ProcessingConsents,
            await BoxAsync(context.ProcessingConsents.AsNoTracking()
                .Where(item => item.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.ConnectionResources,
            await BoxAsync(context.ConnectionResources.AsNoTracking()
                .Where(item => item.Connection.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.ImportJobs,
            await BoxAsync(context.ImportJobs.AsNoTracking()
                .Where(item => item.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.ImportedRecords,
            await BoxAsync(context.ImportedRecords.AsNoTracking()
                .Where(item => item.ImportJob.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.Attachments,
            await BoxAsync(context.Attachments.AsNoTracking()
                .Where(item => item.Transaction.UserId == userId), cancellationToken)),
        new(PersonalDataArchiveCoverage.AuditEntries,
            subjectReference.HasValue
                ? await BoxAsync(context.AuditEntries.AsNoTracking()
                    .Where(item => item.SubjectReference == subjectReference), cancellationToken)
                : []),
        new(PersonalDataArchiveCoverage.ExportHistory,
            await BoxAsync(context.DataExports.AsNoTracking()
                .Where(item => item.UserId == userId), cancellationToken))
    ];

    private Dictionary<string, object?> PortableRecord(
        Type type,
        object record,
        IReadOnlyDictionary<(Type Type, long Id), Guid> publicIds,
        IReadOnlyDictionary<long, string> currencies,
        IReadOnlyDictionary<long, string> attachmentPaths)
    {
        var entityType = context.Model.FindEntityType(type) ??
            throw new InvalidOperationException($"{type.Name} is not mapped.");
        var output = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in entityType.GetProperties().OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            if (property.PropertyInfo is null || property.Name == "Id")
            {
                continue;
            }

            var value = property.PropertyInfo.GetValue(record);
            if (type == typeof(ImportedRecord) &&
                property.Name == nameof(ImportedRecord.RawPayload))
            {
                output[JsonName(property.Name)] = SanitizeImportedPayload((string)value!);
                continue;
            }
            if (property.ClrType == typeof(byte[]))
            {
                if (type == typeof(Connection) && property.Name == nameof(Connection.AccessTokenCipher))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Binary field {type.Name}.{property.Name} requires an explicit portability policy.");
            }

            var foreignKey = property.GetContainingForeignKeys()
                .SingleOrDefault(key => key.Properties.Count == 1 && key.Properties[0] == property);
            if (foreignKey is not null && IsLong(property.ClrType))
            {
                AddPortableReference(output, property, foreignKey, value, publicIds, currencies);
                continue;
            }

            if (IsLong(property.ClrType) && property.Name != nameof(Attachment.SizeInBytes))
            {
                continue;
            }

            output[JsonName(property.Name)] = PortableValue(value, property.ClrType);
        }

        foreach (var navigation in entityType.GetSkipNavigations()
                     .Where(item => item.PropertyInfo is not null)
                     .OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            var identifiers = ((IEnumerable?)navigation.PropertyInfo!.GetValue(record))?
                .Cast<object>()
                .Select(item => PublicId(item))
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
                .Order()
                .ToArray() ?? [];
            output[$"{JsonName(navigation.Name)}Ids"] = identifiers;
        }

        if (record is Attachment attachment)
        {
            output["archivePath"] = attachmentPaths[attachment.Id];
        }

        return output;
    }

    private static void AddPortableReference(
        IDictionary<string, object?> output,
        IProperty property,
        IForeignKey foreignKey,
        object? value,
        IReadOnlyDictionary<(Type Type, long Id), Guid> publicIds,
        IReadOnlyDictionary<long, string> currencies)
    {
        var name = JsonName(property.Name);
        var id = value is null ? (long?)null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        if (foreignKey.PrincipalEntityType.ClrType == typeof(Currency))
        {
            var currencyName = name.EndsWith("Id", StringComparison.Ordinal)
                ? $"{name[..^2]}Code"
                : $"{name}Code";
            output[currencyName] = id.HasValue && currencies.TryGetValue(id.Value, out var code)
                ? code
                : null;
            return;
        }

        output[name] = id.HasValue && publicIds.TryGetValue(
            (foreignKey.PrincipalEntityType.ClrType, id.Value),
            out var publicId)
            ? publicId
            : null;
    }

    private object Schema(Type type)
    {
        var entityType = context.Model.FindEntityType(type) ??
            throw new InvalidOperationException($"{type.Name} is not mapped.");
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in entityType.GetProperties().OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            if (property.PropertyInfo is null || property.Name == "Id" ||
                (property.ClrType == typeof(byte[]) && type == typeof(Connection) &&
                 property.Name == nameof(Connection.AccessTokenCipher)))
            {
                continue;
            }

            if (type == typeof(ImportedRecord) &&
                property.Name == nameof(ImportedRecord.RawPayload))
            {
                properties[JsonName(property.Name)] = new Dictionary<string, object?>();
                continue;
            }

            var foreignKey = property.GetContainingForeignKeys()
                .SingleOrDefault(key => key.Properties.Count == 1 && key.Properties[0] == property);
            if (foreignKey is not null && IsLong(property.ClrType))
            {
                var name = JsonName(property.Name);
                if (foreignKey.PrincipalEntityType.ClrType == typeof(Currency))
                {
                    name = name.EndsWith("Id", StringComparison.Ordinal)
                        ? $"{name[..^2]}Code"
                        : $"{name}Code";
                }
                properties[name] = StringSchema(
                    foreignKey.PrincipalEntityType.ClrType == typeof(Currency) ? null : "uuid",
                    property.IsNullable);
                continue;
            }

            if (IsLong(property.ClrType) && property.Name != nameof(Attachment.SizeInBytes))
            {
                continue;
            }

            properties[JsonName(property.Name)] = ValueSchema(property.ClrType, property.IsNullable);
        }

        foreach (var navigation in entityType.GetSkipNavigations()
                     .OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            properties[$"{JsonName(navigation.Name)}Ids"] = new
            {
                type = "array",
                items = StringSchema("uuid", false)
            };
        }
        if (type == typeof(Attachment))
        {
            properties["archivePath"] = StringSchema(null, false);
        }

        return new Dictionary<string, object?>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = $"Fortuna {type.Name} portability records",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new[] { "schemaVersion", "records" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["schemaVersion"] = new { type = "integer", @const = SchemaVersion },
                ["records"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = properties
                    }
                }
            }
        };
    }

    private static object ValueSchema(Type type, bool nullable)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(decimal))
        {
            return new Dictionary<string, object?>
            {
                ["type"] = nullable ? new[] { "string", "null" } : "string",
                ["pattern"] = "^-?[0-9]+(?:\\.[0-9]+)?$"
            };
        }
        if (underlying == typeof(Guid))
        {
            return StringSchema("uuid", nullable);
        }
        if (underlying == typeof(DateOnly))
        {
            return StringSchema("date", nullable);
        }
        if (underlying == typeof(DateTimeOffset))
        {
            return StringSchema("date-time", nullable);
        }
        if (underlying.IsEnum)
        {
            return new Dictionary<string, object?>
            {
                ["type"] = nullable ? new[] { "string", "null" } : "string",
                ["enum"] = Enum.GetNames(underlying)
            };
        }

        var jsonType = underlying == typeof(bool)
            ? "boolean"
            : underlying == typeof(string) || underlying == typeof(char)
                ? "string"
                : "integer";
        return new Dictionary<string, object?>
        {
            ["type"] = nullable ? new[] { jsonType, "null" } : jsonType
        };
    }

    private static object StringSchema(string? format, bool nullable)
    {
        var schema = new Dictionary<string, object?>
        {
            ["type"] = nullable ? new[] { "string", "null" } : "string"
        };
        if (format is not null)
        {
            schema["format"] = format;
        }
        return schema;
    }

    private static object? PortableValue(object? value, Type declaredType)
    {
        if (value is null)
        {
            return null;
        }
        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (type == typeof(decimal))
        {
            return ((decimal)value).ToString(CultureInfo.InvariantCulture);
        }
        if (type.IsEnum)
        {
            return Enum.GetName(type, value);
        }
        return value;
    }

    private static JsonNode SanitizeImportedPayload(string payload)
    {
        var root = JsonNode.Parse(payload) ?? JsonValue.Create((string?)null)!;
        RedactSensitiveValues(root);
        return root;
    }

    private static void RedactSensitiveValues(JsonNode node)
    {
        if (node is JsonObject value)
        {
            foreach (var property in value.ToArray())
            {
                if (IsSensitiveName(property.Key))
                {
                    value[property.Key] = "[redacted]";
                }
                else if (property.Value is not null)
                {
                    RedactSensitiveValues(property.Value);
                }
            }
            return;
        }

        if (node is JsonArray array)
        {
            foreach (var item in array.Where(item => item is not null))
            {
                RedactSensitiveValues(item!);
            }
        }
    }

    private static bool IsSensitiveName(string name)
    {
        var normalized = new string(name
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return normalized.Contains("password", StringComparison.Ordinal) ||
               normalized.Contains("secret", StringComparison.Ordinal) ||
               normalized.Contains("token", StringComparison.Ordinal) ||
               normalized.Contains("credential", StringComparison.Ordinal) ||
               normalized.Contains("recoverycode", StringComparison.Ordinal);
    }

    private static Dictionary<(Type Type, long Id), Guid> PublicIdLookup(
        IEnumerable<LoadedPart> parts)
    {
        var lookup = new Dictionary<(Type Type, long Id), Guid>();
        foreach (var (descriptor, records) in parts)
        {
            var id = descriptor.EntityType.GetProperty("Id");
            var publicId = descriptor.EntityType.GetProperty("PublicId");
            if (id?.PropertyType != typeof(long) || publicId?.PropertyType != typeof(Guid))
            {
                continue;
            }
            foreach (var record in records)
            {
                lookup[(descriptor.EntityType, (long)id.GetValue(record)!)] =
                    (Guid)publicId.GetValue(record)!;
            }
        }
        return lookup;
    }

    private static Guid? PublicId(object entity) =>
        entity.GetType().GetProperty("PublicId")?.GetValue(entity) as Guid?;

    private static async Task<IReadOnlyList<object>> BoxAsync<TEntity>(
        IQueryable<TEntity> query,
        CancellationToken cancellationToken) where TEntity : class =>
        (await query.ToListAsync(cancellationToken)).Cast<object>().ToArray();

    private static async Task WriteJsonAsync(
        ZipArchive archive,
        string path,
        object value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var target = entry.Open();
        await JsonSerializer.SerializeAsync(target, value, JsonOptions, cancellationToken);
    }

    private static bool IsLong(Type type) =>
        (Nullable.GetUnderlyingType(type) ?? type) == typeof(long);

    private static string JsonName(string value) =>
        JsonNamingPolicy.CamelCase.ConvertName(value);

    private static string SafeFileName(string value)
    {
        var name = Path.GetFileName(value);
        foreach (var character in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(character, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "attachment" : name;
    }

    private sealed record LoadedPart(
        PersonalDataArchivePart Descriptor,
        IReadOnlyList<object> Records);
}

public sealed record PersonalDataArchivePart(string Name, Type EntityType);

/// <summary>The model-level completeness contract; tests compare this registry to EF ownership.</summary>
public static class PersonalDataArchiveCoverage
{
    public static readonly PersonalDataArchivePart Profile = new("profile", typeof(UserProfile));
    public static readonly PersonalDataArchivePart FinancialAccounts = new("financial-accounts", typeof(FinancialAccount));
    public static readonly PersonalDataArchivePart CreditCards = new("credit-cards", typeof(CreditCard));
    public static readonly PersonalDataArchivePart CreditCardStatements = new("credit-card-statements", typeof(CreditCardStatement));
    public static readonly PersonalDataArchivePart Investments = new("investments", typeof(Investment));
    public static readonly PersonalDataArchivePart InvestmentMovements = new("investment-movements", typeof(InvestmentMovement));
    public static readonly PersonalDataArchivePart InvestmentValuations = new("investment-valuations", typeof(InvestmentValuation));
    public static readonly PersonalDataArchivePart Transactions = new("transactions", typeof(FinancialTransaction));
    public static readonly PersonalDataArchivePart Transfers = new("transfers", typeof(Transfer));
    public static readonly PersonalDataArchivePart InstallmentPlans = new("installment-plans", typeof(InstallmentPlan));
    public static readonly PersonalDataArchivePart RecurringTransactions = new("recurring-transactions", typeof(RecurringTransaction));
    public static readonly PersonalDataArchivePart Categories = new("categories", typeof(Category));
    public static readonly PersonalDataArchivePart Tags = new("tags", typeof(Tag));
    public static readonly PersonalDataArchivePart Counterparties = new("counterparties", typeof(Counterparty));
    public static readonly PersonalDataArchivePart Budgets = new("budgets", typeof(Budget));
    public static readonly PersonalDataArchivePart Goals = new("goals", typeof(Goal));
    public static readonly PersonalDataArchivePart Connections = new("connections", typeof(Connection));
    public static readonly PersonalDataArchivePart ProcessingConsents = new("processing-consents", typeof(ProcessingConsent));
    public static readonly PersonalDataArchivePart ConnectionResources = new("connection-resources", typeof(ConnectionResource));
    public static readonly PersonalDataArchivePart ImportJobs = new("import-jobs", typeof(ImportJob));
    public static readonly PersonalDataArchivePart ImportedRecords = new("imported-records", typeof(ImportedRecord));
    public static readonly PersonalDataArchivePart Attachments = new("attachments", typeof(Attachment));
    public static readonly PersonalDataArchivePart AuditEntries = new("audit-entries", typeof(AuditEntry));
    public static readonly PersonalDataArchivePart ExportHistory = new("export-history", typeof(DataExport));

    public static IReadOnlyCollection<PersonalDataArchivePart> Included { get; } =
    [
        Profile, FinancialAccounts, CreditCards, CreditCardStatements, Investments,
        InvestmentMovements, InvestmentValuations, Transactions, Transfers, InstallmentPlans,
        RecurringTransactions, Categories, Tags, Counterparties, Budgets, Goals, Connections,
        ProcessingConsents, ConnectionResources, ImportJobs, ImportedRecords, Attachments,
        AuditEntries, ExportHistory
    ];

    public static IReadOnlySet<Type> SecretOrOperationalExclusions { get; } = new HashSet<Type>
    {
        typeof(LocalAccount),
        typeof(RecoveryCode),
        typeof(AuditSubject),
        typeof(Domain.Jobs.BackgroundJob)
    };
}
