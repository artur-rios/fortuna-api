using System.Reflection;
using System.Text.Json;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.WebApi.Output;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class OpenApiEnumNamesTests
{
    [UnitFact]
    public void GivenPublishedEnumSchemas_WhenInspected_ThenEveryClrMemberNameIsIncluded()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot(), "docs", "openapi", "fortuna.json")));
        var publishedEnums = contract.RootElement.GetProperty("components")
            .GetProperty("schemas")
            .EnumerateObject()
            .Where(schema => schema.Value.TryGetProperty("enum", out _))
            .ToArray();
        var enumTypes = new[]
        {
            typeof(FinancialAccountType).Assembly,
            typeof(CreateFinancialAccountCommand).Assembly,
            typeof(FinancialAccountOutput).Assembly,
            typeof(TableColumnType).Assembly,
            typeof(LivenessOutput).Assembly
        }
            .Distinct()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.IsEnum)
            .GroupBy(type => type.Name)
            .ToDictionary(group => group.Key, group => group.Single());

        Assert.NotEmpty(publishedEnums);
        foreach (var schema in publishedEnums)
        {
            Assert.True(enumTypes.TryGetValue(schema.Name, out var enumType),
                $"Published enum {schema.Name} has no matching CLR enum.");
            var names = schema.Value.GetProperty("x-enum-varnames")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray();

            Assert.Equal(Enum.GetNames(enumType), names);
        }
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
}
