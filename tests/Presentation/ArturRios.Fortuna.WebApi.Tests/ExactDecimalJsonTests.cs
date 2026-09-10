using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.WebApi.Output;
using ArturRios.Fortuna.WebApi.Serialization;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class ExactDecimalJsonTests
{
    private const string DecimalPattern = @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$";

    [UnitTheory]
    [InlineData("0.1")]
    [InlineData("1.005")]
    [InlineData("8017.61")]
    [InlineData("12345678901234.56")]
    public void GivenNonBinaryDecimal_WhenRoundTripped_ThenExactStringValueSurvives(string wire)
    {
        var expected = decimal.Parse(wire, CultureInfo.InvariantCulture);
        var options = Options();

        var serialized = JsonSerializer.Serialize(expected, options);
        var fromString = JsonSerializer.Deserialize<decimal>(serialized, options);
        var fromLegacyNumber = JsonSerializer.Deserialize<decimal>(wire, options);

        Assert.Equal($"\"{wire}\"", serialized);
        Assert.Equal(expected, fromString);
        Assert.Equal(expected, fromLegacyNumber);
    }

    [UnitFact]
    public void GivenNullableDecimalsAndNonMoneyScalars_WhenSerialized_ThenOnlyDecimalsAreStrings()
    {
        var timestamp = DateTimeOffset.Parse("2026-09-10T04:00:00Z");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new WireFixture(1.005m, 8017.61m, 7, timestamp), Options()));

        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("amount").ValueKind);
        Assert.Equal("1.005", document.RootElement.GetProperty("amount").GetString());
        Assert.Equal(JsonValueKind.String,
            document.RootElement.GetProperty("optionalAmount").ValueKind);
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("count").ValueKind);
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("timestamp").ValueKind);
    }

    [UnitTheory]
    [InlineData("\"1e-3\"")]
    [InlineData("\"NaN\"")]
    [InlineData("\"1,000.00\"")]
    [InlineData("\".5\"")]
    [InlineData("\"+1\"")]
    [InlineData("\"01\"")]
    [InlineData("true")]
    public void GivenInvalidDecimalWireValue_WhenRead_ThenItIsRejected(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<decimal>(json, Options()));
    }

    [UnitFact]
    public void GivenPublishedApiModel_WhenInspected_ThenEveryDecimalPropertyIsAStringSchema()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot(), "docs", "openapi", "fortuna.json")));
        var schemas = contract.RootElement.GetProperty("components").GetProperty("schemas");
        var parameters = contract.RootElement.GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .SelectMany(operation => operation.Value.TryGetProperty("parameters", out var values)
                ? values.EnumerateArray()
                : [])
            .ToArray();
        var assemblies = new[]
        {
            typeof(CreateFinancialAccountCommand).Assembly,
            typeof(FinancialAccountOutput).Assembly,
            typeof(LivenessOutput).Assembly
        };
        var decimalProperties = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => !type.ContainsGenericParameters && type.Namespace is not null &&
                (type.Namespace.StartsWith("ArturRios.Fortuna.Command.Input", StringComparison.Ordinal) ||
                 type.Namespace.StartsWith("ArturRios.Fortuna.Command.Output", StringComparison.Ordinal) ||
                 type.Namespace.StartsWith("ArturRios.Fortuna.Query.Input", StringComparison.Ordinal) ||
                 type.Namespace.StartsWith("ArturRios.Fortuna.Query.Output", StringComparison.Ordinal) ||
                 type.Namespace.StartsWith("ArturRios.Fortuna.WebApi.Output", StringComparison.Ordinal)))
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => Nullable.GetUnderlyingType(property.PropertyType) == typeof(decimal) ||
                                   property.PropertyType == typeof(decimal))
                .Select(property => (Type: type, Property: property)))
            .ToArray();

        Assert.NotEmpty(decimalProperties);
        foreach (var (type, property) in decimalProperties)
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (schemas.TryGetProperty(type.Name, out var schema))
            {
                Assert.True(schema.GetProperty("properties").TryGetProperty(name, out var field),
                    $"OpenAPI does not publish {type.Name}.{property.Name}.");
                AssertDecimal(field, $"{type.Name}.{property.Name}");
                continue;
            }

            var matchingParameters = parameters.Where(parameter => string.Equals(
                    parameter.GetProperty("name").GetString(),
                    property.Name,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.NotEmpty(matchingParameters);
            foreach (var parameter in matchingParameters)
            {
                AssertDecimal(parameter.GetProperty("schema"),
                    $"query parameter {property.Name}");
            }
        }

        Assert.DoesNotContain(EnumerateObjects(contract.RootElement), item =>
            item.TryGetProperty("format", out var format) &&
            format.ValueKind == JsonValueKind.String &&
            format.GetString() == "double");
    }

    private static void AssertDecimal(JsonElement field, string name)
    {
        Assert.Equal("string", field.GetProperty("type").GetString());
        Assert.Equal("decimal", field.GetProperty("format").GetString());
        Assert.Equal(DecimalPattern, field.GetProperty("pattern").GetString());
    }

    private static IEnumerable<JsonElement> EnumerateObjects(JsonElement element)
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
            foreach (var nested in EnumerateObjects(child))
            {
                yield return nested;
            }
        }
    }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ExactDecimalJsonConverter());
        return options;
    }

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

    private sealed record WireFixture(
        decimal Amount,
        decimal? OptionalAmount,
        int Count,
        DateTimeOffset Timestamp);
}
