namespace GenWave.SeamIndexGenerator;

/// <summary>
/// Deterministic, non-assembly-qualified display name for a CLR type — <c>Namespace.Type</c> for a
/// closed type, with generic arguments rendered as <c>Outer&lt;Arg&gt;</c> rather than the CLR's
/// backtick+arity encoding. Never <see cref="Type.AssemblyQualifiedName"/> anywhere in SEAMS.md — a
/// strong-name/version token would break byte-identical determinism between two otherwise-identical
/// runs the moment an assembly version bumps, even though nothing about the seam itself changed.
/// </summary>
internal static class FriendlyTypeName
{
    public static string Of(Type type)
    {
        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        var definition = type.GetGenericTypeDefinition();
        var rawName = definition.FullName ?? definition.Name;
        var backtick = rawName.IndexOf('`', StringComparison.Ordinal);
        var name = backtick >= 0 ? rawName[..backtick] : rawName;

        var args = string.Join(", ", type.GetGenericArguments().Select(Of));
        return $"{name}<{args}>";
    }
}
