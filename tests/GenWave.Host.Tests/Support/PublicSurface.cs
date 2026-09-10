using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// Enumerates an assembly's public/protected surface — every exported type (full name, kind, base
/// type, interfaces) plus every public/protected member it declares (constructors, methods,
/// properties with their accessors, fields, events) — as a deterministic, sorted line list.
/// STORY-431 AC6 (SPEC F176.4) diffs this against a committed baseline to prove a release that
/// never touched <c>src/GenWave.Abstractions</c> shipped a byte-identical package surface; a
/// legitimate Abstractions bump regenerates the baseline from the newly published package in the
/// same PR rather than hand-editing it.
/// </summary>
public static class PublicSurface
{
    const BindingFlags DeclaredMemberFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    /// <summary>
    /// Every exported type's surface line plus every public/protected member line it declares,
    /// sorted ordinally. Each line is prefixed with its owning type's full name so a global sort
    /// clusters a type's own lines together without a second grouping pass.
    /// </summary>
    public static IReadOnlyList<string> Of(Assembly assembly)
    {
        var lines = new List<string>();

        foreach (var type in assembly.GetExportedTypes())
        {
            if (IsCompilerGenerated(type))
                continue;

            var owner = TypeName(type);
            lines.Add($"{owner}::TYPE {KindOf(type)} : {BaseTypeNameOf(type)} [{InterfaceListOf(type)}]");
            lines.AddRange(MemberLinesOf(type, owner));
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    static IEnumerable<string> MemberLinesOf(Type type, string owner)
    {
        // Property/event accessor methods are collected in one full, non-lazy pass BEFORE any line
        // is yielded, so no reordering of the loops below can ever double-list an accessor as a
        // bare METHOD line (PLAN T453 ruling).
        var accessorMethods = AccessorMethodsOf(type);

        foreach (var property in type.GetProperties(DeclaredMemberFlags))
        {
            if (IsCompilerGenerated(property))
                continue;

            var accessors = AccessorClauses(property.GetMethod, property.SetMethod);
            if (accessors.Count == 0)
                continue; // neither accessor is visible outside the assembly

            var isStatic = property.GetMethod?.IsStatic ?? property.SetMethod?.IsStatic ?? false;
            yield return $"{owner}::PROPERTY {StaticPrefix(isStatic)}{TypeName(property.PropertyType)} {property.Name} {{ {string.Join(" ", accessors)} }}";
        }

        foreach (var evt in type.GetEvents(DeclaredMemberFlags))
        {
            if (IsCompilerGenerated(evt))
                continue;

            if (!IsPublicOrProtected(evt.AddMethod) && !IsPublicOrProtected(evt.RemoveMethod))
                continue;

            var isStatic = evt.AddMethod?.IsStatic ?? evt.RemoveMethod?.IsStatic ?? false;
            yield return $"{owner}::EVENT {StaticPrefix(isStatic)}{TypeName(evt.EventHandlerType)} {evt.Name}";
        }

        foreach (var field in type.GetFields(DeclaredMemberFlags))
        {
            if (IsCompilerGenerated(field) || !IsPublicOrProtected(field))
                continue;

            var modifier = field.IsLiteral ? " const" : field.IsInitOnly ? " readonly" : string.Empty;
            var value = type.IsEnum && field.IsLiteral ? $" = {field.GetRawConstantValue()}" : string.Empty;
            yield return $"{owner}::FIELD {StaticPrefix(field.IsStatic)}{TypeName(field.FieldType)} {field.Name}{modifier}{value}";
        }

        foreach (var ctor in type.GetConstructors(DeclaredMemberFlags))
        {
            if (IsCompilerGenerated(ctor) || IsModuleInitializer(ctor) || !IsPublicOrProtected(ctor))
                continue;

            yield return $"{owner}::CTOR ({ParameterListOf(ctor)})";
        }

        foreach (var method in type.GetMethods(DeclaredMemberFlags))
        {
            if (IsCompilerGenerated(method) || IsModuleInitializer(method) || !IsPublicOrProtected(method))
                continue;

            if (accessorMethods.Contains(method))
                continue;

            yield return $"{owner}::METHOD {StaticPrefix(method.IsStatic)}{TypeName(method.ReturnType)} {method.Name}({ParameterListOf(method)})";
        }
    }

    /// <summary>
    /// Every property and event accessor method declared on <paramref name="type"/>, for
    /// suppressing the same method being listed a second time as a bare METHOD line.
    /// </summary>
    static HashSet<MethodInfo> AccessorMethodsOf(Type type)
    {
        var methods = new HashSet<MethodInfo>();

        foreach (var property in type.GetProperties(DeclaredMemberFlags))
        {
            if (property.GetMethod is { } getter)
                methods.Add(getter);
            if (property.SetMethod is { } setter)
                methods.Add(setter);
        }

        foreach (var evt in type.GetEvents(DeclaredMemberFlags))
        {
            if (evt.AddMethod is { } add)
                methods.Add(add);
            if (evt.RemoveMethod is { } remove)
                methods.Add(remove);
        }

        return methods;
    }

    static List<string> AccessorClauses(MethodInfo? getter, MethodInfo? setter)
    {
        var clauses = new List<string>();
        if (IsPublicOrProtected(getter))
            clauses.Add($"{Visibility(getter)} get;");
        if (IsPublicOrProtected(setter))
            clauses.Add($"{Visibility(setter)} {SetterKeyword(setter)};");
        return clauses;
    }

    static bool IsPublicOrProtected([NotNullWhen(true)] MethodBase? method) =>
        method is not null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

    static bool IsPublicOrProtected(FieldInfo field) =>
        field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

    static string Visibility(MethodBase method) => method.IsPublic ? "public" : "protected";

    static string StaticPrefix(bool isStatic) => isStatic ? "static " : string.Empty;

    /// <summary>
    /// <c>init;</c> for a C#-compiler-emitted init-only setter (its return parameter carries the
    /// <see cref="System.Runtime.CompilerServices.IsExternalInit"/> required custom modifier),
    /// <c>set;</c> otherwise. An init-only setter demoted to a mutable one is a real contract
    /// change on a published package, so the two must render differently.
    /// </summary>
    static string SetterKeyword(MethodInfo setter) => IsInitOnly(setter) ? "init" : "set";

    static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));

    static bool IsCompilerGenerated(MemberInfo member) =>
        member.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;

    static bool IsModuleInitializer(MethodBase method) =>
        method is MethodInfo m && m.GetCustomAttribute<ModuleInitializerAttribute>() is not null;

    /// <summary>
    /// The type's kind only. <c>abstract</c>/<c>sealed</c> on types and <c>virtual</c>/<c>abstract</c>
    /// on members are a knowing exclusion (PLAN T453 ruling): the surface carries two abstract records
    /// and no virtual or abstract member, so sealing or unsealing one of those types would not diff here.
    /// </summary>
    static string KindOf(Type type)
    {
        if (type.IsInterface) return "interface";
        if (type.IsEnum) return "enum";
        if (typeof(Delegate).IsAssignableFrom(type)) return "delegate";
        if (type.IsValueType) return "struct";
        return "class";
    }

    static string BaseTypeNameOf(Type type)
    {
        var baseType = type.BaseType;
        if (baseType is null || baseType == typeof(object) || baseType == typeof(ValueType) ||
            baseType == typeof(Enum) || baseType == typeof(MulticastDelegate))
        {
            return "none";
        }

        return TypeName(baseType);
    }

    static string InterfaceListOf(Type type) =>
        string.Join(",", type.GetInterfaces().Select(TypeName).OrderBy(n => n, StringComparer.Ordinal));

    static string ParameterListOf(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(ParameterOf));

    static string ParameterOf(ParameterInfo p)
    {
        var text = $"{TypeName(p.ParameterType)} {p.Name}";
        return p.HasDefaultValue ? $"{text} = {DefaultValueLiteral(p.DefaultValue)}" : text;
    }

    /// <summary>
    /// A default parameter value rendered as an invariant literal: <c>null</c> for a null default,
    /// a quoted string for a string default, the member name for an enum default, and
    /// <see cref="Convert.ToString(object?, IFormatProvider?)"/> under
    /// <see cref="CultureInfo.InvariantCulture"/> for everything else (bools, numbers). A changed
    /// default silently changes recompiled callers' behaviour, so it must diff.
    /// </summary>
    static string DefaultValueLiteral(object? value) =>
        value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            Enum e => e.ToString(),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
        };

    /// <summary>
    /// A build-stable type name: namespace-qualified, generic arity rendered as
    /// <c>Outer&lt;Arg1,Arg2&gt;</c> rather than <see cref="Type.FullName"/>'s own
    /// <c>Outer`1[[Arg1, AssemblyName, Version=x.x.x.x, ...]]</c> shape. <see cref="Type.FullName"/>
    /// embeds each generic argument's own <c>AssemblyQualifiedName</c> — including THIS assembly's
    /// version — so two otherwise-identical builds compiled with different assembly versions would
    /// diff on every member whose signature touches a closed generic (<c>Nullable&lt;T&gt;</c>,
    /// <c>Task&lt;T&gt;</c>, <c>IEquatable&lt;T&gt;</c>, …), which defeats the whole point of this
    /// enumerator. <see cref="Type.FullName"/> is also null for a bare generic parameter (e.g. the
    /// <c>T</c> in <c>PagedResult&lt;T&gt;</c>) and for any type built from one, so this recurses on
    /// <see cref="Type.GetGenericArguments"/> instead of trusting <c>FullName</c> at all once a
    /// generic type is involved.
    /// </summary>
    static string TypeName(Type? type)
    {
        if (type is null)
            return "void";
        if (type.IsByRef)
            return TypeName(type.GetElementType()) + "&";
        if (type.IsArray)
            return TypeName(type.GetElementType()) + "[]";
        if (type.IsGenericParameter)
            return type.Name;
        if (!type.IsGenericType)
            return QualifiedNameWithoutArity(type);

        var args = string.Join(",", type.GetGenericArguments().Select(TypeName));
        return $"{QualifiedNameWithoutArity(type)}<{args}>";
    }

    static string QualifiedNameWithoutArity(Type type)
    {
        var name = StripArity(type.Name);
        if (type.IsNested && type.DeclaringType is { } declaring)
            return $"{QualifiedNameWithoutArity(declaring)}+{name}";
        return type.Namespace is null ? name : $"{type.Namespace}.{name}";
    }

    static string StripArity(string name)
    {
        var tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }
}
