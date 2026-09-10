using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ArturRios.Fortuna.WebApi.OpenApi;

/// <summary>Publishes the CLR names paired with integer enum values for client generators.</summary>
public sealed class EnumNamesSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        var enumType = Nullable.GetUnderlyingType(context.Type) ?? context.Type;
        if (!enumType.IsEnum)
        {
            return;
        }

        var names = new JsonArray();
        foreach (var name in Enum.GetNames(enumType))
        {
            names.Add(name);
        }
        if (schema is IOpenApiExtensible extensible)
        {
            extensible.AddExtension("x-enum-varnames", new JsonNodeExtension(names));
        }
    }
}
