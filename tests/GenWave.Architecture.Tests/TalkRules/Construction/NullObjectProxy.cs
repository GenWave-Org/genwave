using System.Reflection;

namespace GenWave.Architecture.Tests.TalkRules.Construction;

/// <summary>
/// A do-nothing stand-in for an interface collaborator: every call returns the empty answer for its return type:
/// default for a value type, empty string, an empty list or dictionary for a collection interface, null for any other
/// reference type, and a completed Task or ValueTask carrying that same answer. The laws judge a rule's declared behaviour over fixture
/// snapshots, never its collaborators, so a rule that needs a store or source is built over one of these.
/// </summary>
public class NullObjectProxy : DispatchProxy
{
    private static readonly MethodInfo CreateMethod = typeof(DispatchProxy)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(Create) && method.GetGenericArguments().Length == 2);

    public static object For(Type contract) =>
        CreateMethod.MakeGenericMethod(contract, typeof(NullObjectProxy)).Invoke(null, null)
        ?? throw new InvalidOperationException($"No stand-in could be made for {contract.Name}.");

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod is null ? null : EmptyAnswer(targetMethod.ReturnType);

    private static object? EmptyAnswer(Type type)
    {
        if (type == typeof(void))
        {
            return null;
        }

        if (type == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var result = EmptyAnswer(type.GetGenericArguments()[0]);
            return typeof(Task).GetMethod(nameof(Task.FromResult))?.MakeGenericMethod(type.GetGenericArguments()[0]).Invoke(null, [result]);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            return Activator.CreateInstance(type, EmptyAnswer(type.GetGenericArguments()[0]));
        }

        if (type == typeof(ValueTask) || type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        if (type == typeof(string))
        {
            return string.Empty;
        }

        return type.IsGenericType ? EmptyCollection(type) : null;
    }

    private static object? EmptyCollection(Type type)
    {
        var arguments = type.GetGenericArguments();
        var list = typeof(List<>).MakeGenericType(arguments[0]);
        if (arguments.Length == 1 && type.IsAssignableFrom(list))
        {
            return Activator.CreateInstance(list);
        }

        if (arguments.Length == 2)
        {
            var dictionary = typeof(Dictionary<,>).MakeGenericType(arguments);
            return type.IsAssignableFrom(dictionary) ? Activator.CreateInstance(dictionary) : null;
        }

        return null;
    }
}
