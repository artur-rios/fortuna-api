using System.Reflection;
using System.Reflection.Emit;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Reads compiled method bodies: the string literals they load and the methods they call. A message
///     constant is compiled into the code that names it as a literal, so this reads what a type's own code
///     can fail with straight from its assembly, independently of scripts/error_responses.py's reading of the
///     source.
/// </summary>
internal static class IlReader
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>
    ///     The bodies of a type's own code: its methods and constructors, and those of the types nested in it,
    ///     where the compiler puts lambdas, local functions and async state machines.
    /// </summary>
    public static IEnumerable<MethodBase> Bodies(Type type) =>
        type.GetMethods(Declared).Cast<MethodBase>()
            .Concat(type.GetConstructors(Declared))
            .Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(Bodies));

    /// <summary>Every string literal the method loads (<c>ldstr</c>).</summary>
    public static IEnumerable<string> Strings(MethodBase method) =>
        Operands(method, OperandType.InlineString)
            .Select(token => method.Module.ResolveString(token));

    /// <summary>Every method the method calls or constructs (<c>call</c>, <c>callvirt</c>, <c>newobj</c>).</summary>
    public static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring
            ? declaring.GetGenericArguments()
            : null;
        var methodArguments = method is { IsGenericMethod: true } ? method.GetGenericArguments() : null;

        return Operands(method, OperandType.InlineMethod)
            .Select(token => method.Module.ResolveMethod(token, typeArguments, methodArguments))
            .OfType<MethodBase>();
    }

    private static IEnumerable<int> Operands(MethodBase method, OperandType wanted)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var position = 0;
        while (position < il.Length)
        {
            short value = il[position++];
            if (value == 0xFE)
            {
                value = unchecked((short)(0xFE00 | il[position++]));
            }

            var code = OpCodesByValue[value];
            if (code.OperandType == wanted)
            {
                yield return BitConverter.ToInt32(il, position);
            }

            position += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, position)),
                _ => 4
            };
        }
    }
}
