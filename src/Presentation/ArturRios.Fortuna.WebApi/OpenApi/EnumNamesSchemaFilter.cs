using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ArturRios.Fortuna.WebApi.OpenApi;

/// <summary>Publishes the CLR names paired with integer enum values for client generators.</summary>
public sealed class EnumNamesSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        // The enum's own schema only. A property or parameter of the enum type is a reference to it,
        // and OpenAPI 3.1, unlike 3.0, would write the names again beside each such $ref.
        var enumType = Nullable.GetUnderlyingType(context.Type) ?? context.Type;
        if (!enumType.IsEnum || schema is not OpenApiSchema concrete)
        {
            return;
        }

        var names = new JsonArray();
        foreach (var name in Enum.GetNames(enumType))
        {
            names.Add(name);
        }
        concrete.AddExtension("x-enum-varnames", new JsonNodeExtension(names));
    }
}
