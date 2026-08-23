using System.Reflection;
using Microsoft.AspNetCore.Authorization;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L9's detector (PLAN T343, T340 review's own mutation-proven carry-forward): scans every type in
/// <paramref name="subjects"/> — class-level AND method-level <see cref="AuthorizeAttribute"/> alike —
/// for one that names <paramref name="schemeName"/> inside its own
/// <see cref="AuthorizeAttribute.AuthenticationSchemes"/>, other than
/// <paramref name="designatedTypeFullName"/>.
///
/// <b>Plain reflection, not an IL-token walk (unlike L7/L8's own <see cref="MemberCallSiteScan"/>
/// workaround).</b> <see cref="AuthorizeAttribute"/> is a REAL runtime attribute the CLR materializes
/// on <see cref="Type.GetCustomAttributes{T}"/>/<see cref="MethodInfo.GetCustomAttributes{T}"/> — a
/// compile-time <c>const string</c> USE (e.g. <c>AuthenticationSchemes = SomeClass.SchemeName</c>)
/// gets folded into the attribute's own stored value by the compiler before this suite ever runs, so
/// there is no separate "did the source reference the constant by name" question to answer at the IL
/// level the way L7/L8's forbidden METHOD CALLS need — the attribute's own resolved
/// <see cref="AuthorizeAttribute.AuthenticationSchemes"/> string already IS the exact value a route's
/// real wired-up authentication accepts.
///
/// <b>Comma-split, exact match — never substring.</b> <see cref="AuthorizeAttribute.AuthenticationSchemes"/>
/// is a comma-separated list (<c>AnnounceTokenAuthenticationDefaults.InScopeSchemes</c> itself is
/// <c>"Cookie,AnnounceToken"</c>) — splitting on <c>,</c> and comparing each trimmed entry by exact,
/// ordinal equality is the honest membership test. A substring check would flag an unrelated scheme
/// whose name merely CONTAINS <paramref name="schemeName"/> as a false positive (the same
/// same-prefix-lookalike hole <see cref="AssemblyReferenceScan.HasFamilyPrefix"/> closes for L1/L5) —
/// this scan closes the identical hole one field over by never doing a substring check at all.
///
/// <b>Scope, precisely (T343 review — documented-unreachable, not closed here).</b> This scan reaches
/// only what the CLR materializes as an <see cref="AuthorizeAttribute"/> — declarative
/// <c>[Authorize(AuthenticationSchemes = ...)]</c> on a type or method. It is BLIND to a hypothetical
/// authentication scheme named through a <see cref="Microsoft.AspNetCore.Authorization.AuthorizationPolicy"/>
/// object built at runtime (e.g. an <c>AuthorizationPolicyBuilder.AddAuthenticationSchemes(...)</c>
/// call composed in <c>Program.cs</c> or a policy factory) rather than an attribute — no such policy
/// exists anywhere in this codebase today (every named policy in <c>AuthorizationPolicies</c> carries
/// no scheme list of its own), so the gap is real but currently unreachable, not a live hole. A future
/// policy-object scheme list would need its own, separate fitness law; this one only ever promises to
/// fence the attribute shape.
/// </summary>
internal static class AnnounceSchemeFence
{
    public static IReadOnlyList<LawViolation> FindViolations(
        IEnumerable<Type> subjects, string schemeName, string designatedTypeFullName)
    {
        var violations = new List<LawViolation>();

        foreach (var type in subjects)
        {
            if (type.FullName == designatedTypeFullName) continue;

            var classHit = NamesScheme(type.GetCustomAttributes<AuthorizeAttribute>(inherit: false), schemeName);

            var methodHit = type
                .GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(m => NamesScheme(m.GetCustomAttributes<AuthorizeAttribute>(inherit: false), schemeName));

            // One violation per offending TYPE, not per attribute occurrence — a type carrying the
            // hazard at both class and method level is still one thing for a reviewer to fix, the
            // same "one violation per outermost declaring type" posture HostNamespaceTripwire's own
            // remarks take for L5.
            if (classHit || methodHit)
                violations.Add(Violation(type, schemeName));
        }

        return violations;
    }

    static bool NamesScheme(IEnumerable<AuthorizeAttribute> attributes, string schemeName) =>
        attributes.Any(a => a.AuthenticationSchemes is { } schemes
            && schemes.Split(',').Select(s => s.Trim()).Contains(schemeName, StringComparer.Ordinal));

    static LawViolation Violation(Type type, string schemeName) => new(
        LawId.L9,
        type.FullName ?? type.Name,
        $"names \"{schemeName}\" inside an [Authorize(AuthenticationSchemes = ...)] list — only the designated type may");
}
