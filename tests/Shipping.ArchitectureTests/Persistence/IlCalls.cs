using System.Reflection;

namespace Shipping.ArchitectureTests.Persistence;

/// <summary>The methods a compiled method calls, read from its IL: <c>call</c>, <c>callvirt</c> and <c>newobj</c>.</summary>
internal static class IlCalls
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>Every call made anywhere in the assembly, async state machines and lambdas included.</summary>
    public static IEnumerable<(MethodBase Caller, MethodBase Called)> In(Assembly assembly) =>
        assembly.GetTypes()
            .SelectMany(static type => type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            .SelectMany(static method => From(method).Select(called => (method, called)));

    public static IEnumerable<MethodBase> From(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (var offset = 0; offset + 4 < il.Length; offset++)
        {
            if (il[offset] is not (0x28 or 0x6F or 0x73)) continue; // call, callvirt, newobj
            MethodBase? called = null;
            try
            {
                called = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset + 1),
                    method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
            }
            catch (ArgumentException) { /* A byte in another operand can equal a call opcode. */ }
            catch (BadImageFormatException) { /* Same: the token after it was not a method token. */ }
            if (called is not null) yield return called;
        }
    }
}
