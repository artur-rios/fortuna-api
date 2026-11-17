using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ArturRios.Fortuna.WebApi.OpenApi;

/// <summary>
///     Marks a property that refers to another schema nullable when its CLR member is: a nullable
///     reference (<c>Output?</c>, the envelope's <c>T? Data</c>) or a nullable value type (<c>Enum?</c>).
///     The API writes <c>null</c> in such a property. Swashbuckle marks inline properties (strings,
///     arrays, <c>int?</c>) nullable already, which OpenAPI 3.1 writes as <c>type: [T, "null"]</c>, but a
///     property that refers to a schema is a bare <c>$ref</c>, published as never null. It becomes
///     <c>anyOf</c> over the reference and the <c>null</c> type. The JSON on the wire is unchanged.
/// </summary>
public sealed class NullableReferenceSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema { Properties.Count: > 0 } concrete)
        {
            return;
        }

        foreach (var property in context.Type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ??
                       JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (concrete.Properties!.TryGetValue(name, out var published) &&
                published is OpenApiSchemaReference reference &&
                IsNullable(property))
            {
                concrete.Properties[name] = new OpenApiSchema
                {
                    AnyOf = [reference, new OpenApiSchema { Type = JsonSchemaType.Null }]
                };
            }
        }
    }

    private static bool IsNullable(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
        {
            return true;
        }

        if (property.PropertyType.IsValueType)
        {
            return false;
        }

        var info = new NullabilityInfoContext().Create(property);

        return info.ReadState == NullabilityState.Nullable ||
               info.WriteState == NullabilityState.Nullable;
    }
}
