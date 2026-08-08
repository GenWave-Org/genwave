using System.Reflection;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L5's detector (STORY-292 AC1/AC2): "no type in the subject set has a namespace that is, or is
/// nested under, a <see cref="HostReservedNamespaces"/> entry" is a pure namespace-membership
/// question — not a dependency-graph one — so plain <see cref="System.Reflection"/> over
/// <c>Assembly.GetTypes()</c> already answers it directly, the same reasoning
/// <see cref="AbstractionsImmutability"/> gives for L4-immutability's member-shape question.
///
/// <b>Why this needs no metadata-table workaround, unlike L3.</b> <see cref="HttpClientMetadataScan"/>
/// exists because ArchUnitNET's OWN type graph — built by walking top-down from each namespace — never
/// surfaces a compiler-generated type nested inside another compiler-generated type (an async lambda's
/// closure-within-state-machine). That blindness is specific to how ArchUnitNET builds its graph, not
/// to reflection: <c>Type.Namespace</c> on any nested type (compiler-generated or not) already resolves
/// to its OUTERMOST enclosing type's namespace — verified experimentally (a probe assembly with an
/// async lambda under <c>GenWave.Host.Context</c> reports <c>Namespace == "GenWave.Host.Context"</c>
/// for the lambda's closure class, its state machine, AND the state machine nested inside the closure —
/// every compiler-generated layer). <see cref="Assembly.GetTypes"/> itself returns every type defined
/// in the assembly, including types nested inside other types (its own documented contract), so there
/// is no separate "walk the metadata tables directly" step to add here at all.
///
/// <b>Attribution.</b> Still rolled up to the outermost declaring type before reporting, for the same
/// reason L3 does it: a compiler-generated name (<c>Foo+&lt;&gt;c__DisplayClass0_0+&lt;&lt;Bar&gt;
/// b__0&gt;d</c>) is meaningless to a reviewer, and several compiler-generated types under the same
/// reserved namespace would otherwise report as several violations of the same one thing.
///
/// <b>Segment-boundary matching.</b> "Namespace is, or is nested under, a reservation" is exactly
/// <see cref="AssemblyReferenceScan.HasFamilyPrefix"/>'s "assembly name is, or is segmented under, a
/// family" question — reused directly (not re-implemented) so a hypothetical
/// <c>GenWave.Host.ContextLike</c> namespace is never wrongly caught by the <c>GenWave.Host.Context</c>
/// reservation, the exact same-prefix-lookalike hole that check already closes and is already probed
/// against (Story290_DependencyLaws.cs).
/// </summary>
internal static class HostNamespaceTripwire
{
    /// <summary>Every <paramref name="subjects"/> type whose namespace lands under a
    /// <paramref name="reservations"/> entry, one violation per outermost declaring type (whichever
    /// reservation its scan matches first — a type nested inside two reserved namespaces at once is
    /// not a shape this suite's namespaces can produce).</summary>
    public static IReadOnlyList<LawViolation> FindViolations(
        IEnumerable<Type> subjects, IReadOnlyList<HostNamespaceReservation> reservations)
    {
        var hits = new List<(Type OutermostType, HostNamespaceReservation Reservation)>();

        foreach (var type in subjects)
        {
            if (type.Namespace is not { } typeNamespace)
                continue; // The global namespace (e.g. a top-level-statement Program) is never reserved.

            var reservation = reservations.FirstOrDefault(
                r => AssemblyReferenceScan.HasFamilyPrefix(typeNamespace, r.ReservedNamespace));
            if (reservation is not null)
                hits.Add((OutermostDeclaringType(type), reservation));
        }

        return hits
            .GroupBy(hit => hit.OutermostType)
            .Select(group => group.First())
            .Select(hit => new LawViolation(
                LawId.L5,
                hit.OutermostType.FullName ?? hit.OutermostType.Name,
                $"namespace reserved by the graduation rule ({hit.Reservation.RulingReference}) — {hit.Reservation.Reason}"))
            .ToList();
    }

    private static Type OutermostDeclaringType(Type type)
    {
        var current = type;
        while (current.DeclaringType is { } declaringType)
            current = declaringType;

        return current;
    }
}
