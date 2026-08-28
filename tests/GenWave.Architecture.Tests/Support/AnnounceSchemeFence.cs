using System.Reflection;
using Microsoft.AspNetCore.Authorization;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L9's detector (PLAN T343, T340 review's own mutation-proven carry-forward; widened to a SET of
/// designated carriers at PLAN T351, SPEC F145.6, STORY-366): scans every type in
/// <paramref name="subjects"/> — class-level AND method-level <see cref="AuthorizeAttribute"/> alike —
/// for one that names <c>schemeName</c> inside its own
/// <see cref="AuthorizeAttribute.AuthenticationSchemes"/>, other than a type named in the caller's own
/// designated set (today, exactly two: <c>AnnouncementsController</c> and
/// <c>AnnouncementNowPlayingController</c> — see <c>Story360_AnnounceSchemeFence.cs</c>'s and
/// <c>Story366_AnnounceSchemeFenceTwoCarriers.cs</c>'s own real-host facts). The single-name overload
/// below is kept — never removed — for callers (this suite's own synthetic-fixture facts) that only
/// ever need to exclude ONE stand-in type; it is a thin wrapper over the set-based overload, not a
/// second implementation to keep in sync.
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
    /// <summary>The real host's own designated carrier set (SPEC F145.6, PLAN T351) — the ONE copy
    /// <c>Story360_AnnounceSchemeFence.cs</c>'s mechanism-sanity facts and
    /// <c>Story366_AnnounceSchemeFenceTwoCarriers.cs</c>'s real-host and third-carrier facts all
    /// share, so the two spec files never risk drifting apart on which types the law designates.</summary>
    public static readonly IReadOnlyList<string> DesignatedCarriers =
    [
        "GenWave.Host.Api.AnnouncementsController",
        "GenWave.Host.Api.AnnouncementNowPlayingController",
    ];

    /// <summary>Single-carrier convenience overload — wraps <paramref name="designatedTypeFullName"/>
    /// in a one-element set and forwards. Kept so a caller excluding only one stand-in type (this
    /// suite's own synthetic-fixture facts) never has to build a collection literal for it.</summary>
    public static IReadOnlyList<LawViolation> FindViolations(
        IEnumerable<Type> subjects, string schemeName, string designatedTypeFullName) =>
        FindViolations(subjects, schemeName, [designatedTypeFullName]);

    public static IReadOnlyList<LawViolation> FindViolations(
        IEnumerable<Type> subjects, string schemeName, IReadOnlyCollection<string> designatedTypeFullNames)
    {
        var violations = new List<LawViolation>();

        foreach (var type in subjects)
        {
            if (designatedTypeFullNames.Any(designated => designated == type.FullName)) continue;

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
                violations.Add(Violation(type, schemeName, designatedTypeFullNames));
        }

        return violations;
    }

    static bool NamesScheme(IEnumerable<AuthorizeAttribute> attributes, string schemeName) =>
        attributes.Any(a => a.AuthenticationSchemes is { } schemes
            && schemes.Split(',').Select(s => s.Trim()).Contains(schemeName, StringComparer.Ordinal));

    static LawViolation Violation(Type type, string schemeName, IReadOnlyCollection<string> designatedTypeFullNames) => new(
        LawId.L9,
        type.FullName ?? type.Name,
        $"names \"{schemeName}\" inside an [Authorize(AuthenticationSchemes = ...)] list — only the " +
        $"designated carriers ({string.Join(", ", designatedTypeFullNames)}) may");
}
