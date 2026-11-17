using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.WebApi.Controllers;
using ArturRios.Fortuna.WebApi.Output;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Keeps docs/openapi/fortuna.json honest about what the API answers, since client generators
///     trust it: the success statuses each operation can answer, which request bodies are required,
///     and which properties can be null, in OpenAPI 3.1's terms. The check job keeps the committed
///     document what the build generates, so these read the committed one.
/// </summary>
public sealed partial class OpenApiDocumentAccuracyTests
{
    private static readonly string[] Methods = ["get", "post", "put", "patch", "delete"];

    // What the handlers answer on success, as the HTTP tests assert it: every operation not listed
    // answers 200 only. Two codes mean the outcome decides (a reused counterparty or a replaced
    // rate is 200, a new one 201; an export small enough to stream is the file itself, 200).
    private static readonly Dictionary<string, int[]> NonDefaultSuccessStatuses = new()
    {
        ["POST /api/accounts"] = [201],
        ["POST /api/auth/register"] = [202],
        ["POST /api/budgets"] = [201],
        ["POST /api/categories"] = [201],
        ["POST /api/connections"] = [201],
        ["POST /api/connections/{id}/sync"] = [202],
        ["POST /api/counterparties"] = [200, 201],
        ["POST /api/credit-cards"] = [201],
        ["POST /api/exchange-rates"] = [200, 201],
        ["POST /api/exports"] = [200, 202],
        ["POST /api/goals"] = [201],
        ["POST /api/import-jobs/{id}/retry"] = [202],
        ["POST /api/imports/excel"] = [202],
        ["POST /api/imports/pdf"] = [202],
        ["POST /api/installment-plans"] = [201],
        ["POST /api/investments"] = [201],
        ["POST /api/local-accounts"] = [201],
        ["POST /api/me/data-export"] = [202],
        ["POST /api/recurring-transactions"] = [201],
        ["POST /api/tags"] = [201],
        ["POST /api/transactions"] = [201],
        ["POST /api/transactions/{id}/attachments"] = [201],
        ["POST /api/transfers"] = [201]
    };

    [UnitFact]
    public void GivenEveryOperation_WhenDocumented_ThenItDeclaresExactlyTheSuccessStatusesItAnswers()
    {
        using var contract = Contract();

        var documented = Operations(contract)
            .ToDictionary(operation => operation.Key, operation => SuccessStatuses(operation.Value));

        Assert.NotEmpty(documented);
        foreach (var (key, statuses) in documented)
        {
            var expected = NonDefaultSuccessStatuses.GetValueOrDefault(key, [200]);
            Assert.True(expected.SequenceEqual(statuses),
                $"{key} documents {string.Join(", ", statuses)} but answers {string.Join(", ", expected)}.");
        }

        Assert.All(NonDefaultSuccessStatuses.Keys, key => Assert.Contains(key, documented.Keys));
    }

    [UnitFact]
    public void GivenControllerStatusMaps_WhenDocumented_ThenEverySuccessStatusTheyMapIsDeclaredByTheController()
    {
        using var contract = Contract();
        var documentedByTag = Operations(contract)
            .GroupBy(operation => operation.Value.GetProperty("tags")[0].GetString()!)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(operation => SuccessStatuses(operation.Value)).ToHashSet());

        var controllers = Controllers().Where(type => type.IsSubclassOf(typeof(FortunaController))).ToArray();

        Assert.NotEmpty(controllers);
        foreach (var controller in controllers)
        {
            var mapped = controller
                .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(field => field.FieldType == typeof(IReadOnlyDictionary<string, int>))
                .SelectMany(field => ((IReadOnlyDictionary<string, int>)field.GetValue(null)!).Values)
                .Where(status => status is > 200 and < 300)
                .ToHashSet();
            var tag = controller.Name[..^"Controller".Length];
            var declared = documentedByTag.GetValueOrDefault(tag, [])
                .Where(status => status != 200)
                .ToHashSet();

            Assert.True(mapped.SetEquals(declared),
                $"{controller.Name} maps success statuses [{string.Join(", ", mapped.Order())}] " +
                $"but its operations declare [{string.Join(", ", declared.Order())}].");
        }
    }

    [UnitFact]
    public void GivenFromBodyActions_WhenDocumented_ThenABodyIsRequiredUnlessTheParameterMayBeOmitted()
    {
        using var contract = Contract();
        var operations = Operations(contract).ToDictionary(operation => operation.Key, operation => operation.Value);
        var nullability = new NullabilityInfoContext();
        var checkedBodies = 0;

        foreach (var (key, action) in Actions())
        {
            var body = action.GetParameters()
                .SingleOrDefault(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() is not null);
            if (body is null)
            {
                continue;
            }

            var mayBeOmitted = body.HasDefaultValue ||
                               nullability.Create(body).WriteState == NullabilityState.Nullable;
            var requestBody = operations[key].GetProperty("requestBody");
            var required = requestBody.TryGetProperty("required", out var value) && value.GetBoolean();

            Assert.True(required != mayBeOmitted,
                $"{key} binds [FromBody] {body.ParameterType.Name}, so its body must " +
                $"{(mayBeOmitted ? "stay optional" : "be required")}.");
            checkedBodies++;
        }

        Assert.True(checkedBodies > 50, $"Only {checkedBodies} [FromBody] actions were found.");
        Assert.False(operations["POST /api/exchange-rates/sync"].GetProperty("requestBody")
            .TryGetProperty("required", out _));
        Assert.True(operations["POST /api/accounts"].GetProperty("requestBody")
            .GetProperty("required").GetBoolean());
    }

    [UnitFact]
    public void GivenEnvelopeSchemas_WhenDocumented_ThenTheirDataIsNullable()
    {
        using var contract = Contract();
        var envelopes = Schemas(contract)
            .Where(schema => schema.Name.EndsWith("DataOutput", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(envelopes);
        foreach (var envelope in envelopes)
        {
            var data = envelope.Value.GetProperty("properties").GetProperty("data");

            Assert.True(IsNullable(data), $"{envelope.Name}.data is null on a failure but is not nullable.");
        }
    }

    [UnitFact]
    public void GivenNullableClrProperties_WhenDocumented_ThenEachPublishedOneIsNullable()
    {
        using var contract = Contract();
        var schemas = Schemas(contract).ToDictionary(schema => schema.Name, schema => schema.Value);
        var nullability = new NullabilityInfoContext();
        var published = 0;
        var nullableReferences = 0;

        foreach (var type in ContractTypes())
        {
            if (!schemas.TryGetValue(type.Name, out var schema) ||
                !schema.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ??
                           JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                if (!properties.TryGetProperty(name, out var publishedProperty))
                {
                    continue;
                }

                published++;
                var info = nullability.Create(property);
                var nullable = Nullable.GetUnderlyingType(property.PropertyType) is not null ||
                               (!property.PropertyType.IsValueType &&
                                (info.ReadState == NullabilityState.Nullable ||
                                 info.WriteState == NullabilityState.Nullable));
                if (!nullable)
                {
                    continue;
                }

                Assert.True(IsNullable(publishedProperty),
                    $"{type.Name}.{name} can be null but the document does not say so.");
                if (publishedProperty.TryGetProperty("anyOf", out _))
                {
                    nullableReferences++;
                }
            }
        }

        Assert.True(published > 1000, $"Only {published} published properties were checked.");
        Assert.True(nullableReferences > 0, "No nullable property refers to another schema.");
    }

    [UnitTheory]
    [InlineData("CreateFinancialAccountCommandOutputDataOutput", "data")]
    [InlineData("TransactionDrillDownOutput", "transaction")]
    [InlineData("ReconcileTransactionCommandOutput", "reconciliation")]
    [InlineData("InvestmentOutput", "rateSource")]
    [InlineData("ConvertedCurrencyGroupOutput", "rateSource")]
    public void GivenKnownNullableReference_WhenDocumented_ThenItIsAnyOfTheReferenceAndNull(
        string schemaName,
        string propertyName)
    {
        using var contract = Contract();

        var property = Schemas(contract).Single(schema => schema.Name == schemaName).Value
            .GetProperty("properties").GetProperty(propertyName);

        Assert.True(IsNullableReference(property), $"{schemaName}.{propertyName} is {property}.");
        Assert.Equal(["anyOf"], property.EnumerateObject().Select(keyword => keyword.Name));
    }

    [UnitFact]
    public void GivenTheDocument_WhenRead_ThenItIsOpenApi31()
    {
        using var contract = Contract();

        Assert.StartsWith("3.1.", contract.RootElement.GetProperty("openapi").GetString());
    }

    [UnitFact]
    public void GivenNullableSchemas_WhenDocumented_ThenTheyUseTheNullTypeAndNoNullableKeyword()
    {
        using var contract = Contract();
        var objects = Objects(contract.RootElement).ToArray();
        var nullableReferences = objects.Where(item => item.TryGetProperty("anyOf", out _)).ToArray();

        // OpenAPI 3.1 dropped 3.0's nullable keyword, which a generator reading 3.1 ignores; a
        // reference is made nullable as anyOf over it and the null type, and nothing else.
        Assert.DoesNotContain(objects, item => item.TryGetProperty("nullable", out _));
        Assert.DoesNotContain(objects, item => item.TryGetProperty("allOf", out _));
        Assert.NotEmpty(nullableReferences);
        Assert.All(nullableReferences, item =>
            Assert.True(IsNullableReference(item), $"An anyOf is not a reference or null: {item}"));
        Assert.Contains(objects, item => item.TryGetProperty("type", out var type) &&
                                         type.ValueKind == JsonValueKind.Array &&
                                         type.EnumerateArray().Any(member => member.GetString() == "null"));
    }

    /// <summary>
    ///     Whether the schema admits <c>null</c>: its <c>type</c> includes <c>"null"</c>, it is
    ///     <c>anyOf</c> a reference and the null type, or it is untyped (<c>object?</c> publishes
    ///     <c>{}</c>, which holds any value, <c>null</c> included).
    /// </summary>
    private static bool IsNullable(JsonElement property) =>
        (property.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Array &&
         type.EnumerateArray().Any(member => member.GetString() == "null")) ||
        IsNullableReference(property) ||
        !(property.TryGetProperty("type", out _) || property.TryGetProperty("$ref", out _) ||
          property.TryGetProperty("anyOf", out _) || property.TryGetProperty("allOf", out _));

    /// <summary><c>anyOf: [{$ref}, {type: "null"}]</c>, OpenAPI 3.1's nullable reference.</summary>
    private static bool IsNullableReference(JsonElement schema) =>
        schema.TryGetProperty("anyOf", out var anyOf) &&
        anyOf.GetArrayLength() == 2 &&
        anyOf[0].EnumerateObject().Select(keyword => keyword.Name).SequenceEqual(["$ref"]) &&
        anyOf[0].GetProperty("$ref").GetString()!.StartsWith("#/components/schemas/", StringComparison.Ordinal) &&
        anyOf[1].EnumerateObject().Select(keyword => keyword.Name).SequenceEqual(["type"]) &&
        anyOf[1].GetProperty("type").GetString() == "null";

    private static IEnumerable<JsonElement> Objects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
        }
        foreach (var child in element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Select(item => item.Value),
            JsonValueKind.Array => element.EnumerateArray(),
            _ => []
        })
        {
            foreach (var nested in Objects(child))
            {
                yield return nested;
            }
        }
    }

    private static int[] SuccessStatuses(JsonElement operation) =>
        operation.GetProperty("responses")
            .EnumerateObject()
            .Select(response => int.Parse(response.Name))
            .Where(status => status is >= 200 and < 300)
            .Order()
            .ToArray();

    private static IEnumerable<KeyValuePair<string, JsonElement>> Operations(JsonDocument contract) =>
        contract.RootElement.GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => Methods.Contains(operation.Name))
                .Select(operation => KeyValuePair.Create(
                    $"{operation.Name.ToUpperInvariant()} {path.Name}",
                    operation.Value)));

    private static IEnumerable<JsonProperty> Schemas(JsonDocument contract) =>
        contract.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject();

    private static IEnumerable<Type> Controllers() =>
        typeof(FortunaController).Assembly.GetExportedTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true } && type.IsSubclassOf(typeof(ControllerBase)));

    /// <summary>Each controller action, keyed like <see cref="Operations" />.</summary>
    private static IEnumerable<KeyValuePair<string, MethodInfo>> Actions() =>
        Controllers().SelectMany(controller =>
        {
            var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;

            return controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .SelectMany(action => action.GetCustomAttributes<HttpMethodAttribute>()
                    .SelectMany(route => route.HttpMethods.Select(method => KeyValuePair.Create(
                        $"{method} {DocumentPath(prefix, route.Template)}",
                        action))));
        });

    private static string DocumentPath(string prefix, string? template)
    {
        var path = string.IsNullOrEmpty(template) ? prefix
            : template.StartsWith('/') || template.StartsWith('~') ? template.TrimStart('~', '/')
            : string.IsNullOrEmpty(prefix) ? template
            : $"{prefix}/{template}";

        return "/" + RouteParameter().Replace(path.Trim('/'), "{$1}");
    }

    private static IEnumerable<Type> ContractTypes() =>
        new[]
            {
                typeof(CreateFinancialAccountCommand).Assembly,
                typeof(FinancialAccountOutput).Assembly,
                typeof(LivenessOutput).Assembly
            }
            .Distinct()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type is { IsClass: true, ContainsGenericParameters: false, Namespace: not null } &&
                           (type.Namespace.StartsWith("ArturRios.Fortuna.Command.", StringComparison.Ordinal) ||
                            type.Namespace.StartsWith("ArturRios.Fortuna.Query.", StringComparison.Ordinal) ||
                            type.Namespace.StartsWith("ArturRios.Fortuna.WebApi.Output", StringComparison.Ordinal)));

    private static JsonDocument Contract() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "openapi", "fortuna.json")));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "docs", "openapi", "fortuna.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }

    [GeneratedRegex(@"\{(\w+)(?::[^}]*)?\??\}")]
    private static partial Regex RouteParameter();
}
