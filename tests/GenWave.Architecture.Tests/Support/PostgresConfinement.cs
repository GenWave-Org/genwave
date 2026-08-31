using ArchUnitNET.Domain;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ArchUnitArchitecture = ArchUnitNET.Domain.Architecture;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L2's detector: ArchUnitNET's dependency graph, which — unlike the hand-rolled assembly-level
/// scan <see cref="AssemblyReferenceScan"/> runs for L1/L4-references — walks INSIDE method bodies
/// (verified experimentally at T211: a fixture method calling <c>new NpgsqlConnection(...)</c> is
/// caught, a field/property type is caught). That method-body reach is exactly what "type usage
/// inside method bodies" needs and is sound here because the forbidden set is exactly two
/// well-known assemblies (Npgsql, Dapper), both always loaded up front — see
/// <see cref="AssemblyReferenceScan"/>'s remarks for why the same library-based technique is NOT
/// used for L1/L4-references, whose forbidden/allowed sets can't be enumerated that way.
/// </summary>
internal static class PostgresConfinement
{
    /// <summary>T357/T372's own narrowing (the T355 review LOW-3 carry-forward
    /// <see cref="RepositoryLayer"/>'s own remarks name): inside <c>Garden</c>, ONLY a
    /// <c>*Repository</c>-named type may touch Npgsql/Dapper — closing the gap a future
    /// non-repository type dropped into <c>Garden/</c> (e.g. a <c>GardenerService</c>) would
    /// otherwise open unnoticed. Catalog/Station stay NAMESPACE-scoped (not narrowed the same way)
    /// — see <see cref="RepositoryLayer"/>'s own remarks for why narrowing them too would be
    /// silently widening what L2 forbids, not what T357 asked for.
    ///
    /// <para>
    /// Built as its OWN nested conjunction, composed into <see cref="RepositoryLayer"/> below via
    /// <c>.Or().Are(...)</c> rather than a bare `.Or().ResideInNamespace(...).And()
    /// .HaveNameEndingWith(...)` tail. Verified by decompiling <c>PredicateManager&lt;T&gt;
    /// .GetObjects</c> (TngTech.ArchUnitNET 0.13.4): a <c>GivenTypesConjunction</c> is a flat,
    /// left-to-right fold — `.And()`/`.Or()` calls have no operator precedence and each one
    /// combines with the WHOLE running set built so far, not just the immediately preceding term.
    /// Appending `.And().HaveNameEndingWith("Repository")` to the end of the existing Catalog/
    /// Station `.Or()` chain would therefore intersect the ENTIRE running set with "ends in
    /// Repository", wrongly narrowing Catalog/Station too — both namespaces hold plenty of
    /// non-Repository types that legitimately touch Npgsql/Dapper today (e.g.
    /// <c>DateOnlyTypeHandler</c>/<c>AnnouncementStateTypeHandler</c> extend
    /// <c>SqlMapper.TypeHandler&lt;T&gt;</c>; <c>BoothLogServiceCollectionExtensions</c>/
    /// <c>PersonaServiceCollectionExtensions</c> construct <c>NpgsqlDataSourceBuilder</c> directly).
    /// <c>.Are(IObjectProvider&lt;IType&gt;)</c> re-evaluates its own nested conjunction
    /// independently against the architecture and intersects ONLY that result into the running
    /// set (<c>TypePredicatesDefinition&lt;T&gt;.Are</c>'s own implementation) — the one
    /// composition primitive here that lets an `.And()` stay scoped to just its own term.
    /// </para>
    ///
    /// <para>
    /// <b>T380's own widening:</b> <c>ResideInNamespaceMatching</c> (anchored <c>^...$</c>,
    /// verified against ArchUnitNET 0.13.4's own decompiled <c>FullNameMatches</c> — a plain,
    /// un-anchored <see cref="System.Text.RegularExpressions.Regex.IsMatch(string, string)"/>) —
    /// not the exact-match <c>ResideInNamespace</c> T357 shipped with, which only ever matched the
    /// literal <c>GenWave.MediaLibrary.Garden</c> namespace itself. <c>Garden.FileActions.FileActionRepository</c>
    /// (SPEC F154.6, F154.7; STORY-379; PLAN T380, gh-#529) lives one level deeper, in
    /// <c>GenWave.MediaLibrary.Garden.FileActions</c> — a real, committed sub-namespace this law
    /// must recognise, not a probe fixture. Matching every CURRENT AND FUTURE sub-namespace of
    /// Garden is the correct reading of "inside Garden, only a <c>*Repository</c>-named type may
    /// touch Npgsql/Dapper" (this class's own header line) — the law was never meant to stop at one
    /// folder depth.
    /// </para></summary>
    private static readonly IObjectProvider<IType> GardenRepositoriesOnly = Types().That()
        .ResideInNamespaceMatching(@"^GenWave\.MediaLibrary\.Garden(\..+)?$")
        .And().HaveNameEndingWith("Repository");

    /// <summary>MediaLibrary's actual repository layer: the namespaces (Catalog, Station) or,
    /// inside Garden, the specific <c>*Repository</c>-named types (<see cref="GardenRepositoriesOnly"/>)
    /// that may touch Npgsql/Dapper. Catalog/Station discovered at T211 adoption; Garden added at
    /// T355 (SPEC F149.1-F149.3, STORY-367, gh-#529) for <c>MediaRotationRepository</c> — the
    /// Library Gardener's own home per ARCHITECTURE.md, confined by this same law like every other
    /// repository namespace here — then narrowed to TYPE-scoped at T357 (the T355 review LOW-3
    /// carry-forward <see cref="GardenRepositoriesOnly"/>'s own remarks explain in full): a future
    /// non-repository type dropped into <c>Garden/</c> now still opens Dapper/Npgsql visibly,
    /// rather than inheriting the whole namespace's allowance the way Catalog/Station's own
    /// NAMESPACE-scoped entries still do (unchanged since T211 — narrowing those two the same way
    /// is not mechanically clean with this law's flat-fold detector; see
    /// <see cref="GardenRepositoriesOnly"/>'s own remarks for why).</summary>
    public static readonly IObjectProvider<IType> RepositoryLayer = Types().That()
        .ResideInNamespace("GenWave.MediaLibrary.Catalog")
        .Or().ResideInNamespace("GenWave.MediaLibrary.Station")
        .Or().Are(GardenRepositoriesOnly);

    /// <summary>Evaluates "<paramref name="subjects"/> must not depend on Npgsql or Dapper" against
    /// <paramref name="architecture"/>, returning one <see cref="LawViolation"/> per offending type.
    /// Both assemblies must have been passed to the architecture's <c>LoadAssemblies</c> call, or
    /// they resolve as an empty target and every violation false-passes (the same pitfall
    /// <see cref="AssemblyReferenceScan"/>'s remarks describe).</summary>
    public static IReadOnlyList<LawViolation> FindViolations(ArchUnitArchitecture architecture, GivenTypesConjunction subjects)
    {
        var forbidden = Types().That()
            .ResideInAssembly(ProductionAssemblies.Npgsql)
            .Or().ResideInAssembly(ProductionAssemblies.Dapper);

        var rule = subjects.Should().NotDependOnAny(forbidden);

        return rule.Evaluate(architecture)
            .Where(r => !r.Passed)
            .Select(ToViolation)
            .ToList();
    }

    private static LawViolation ToViolation(ArchUnitNET.Fluent.EvaluationResult result)
    {
        var member = result.EvaluatedObjectIdentifier.ToString() ?? "<unknown>";
        return new LawViolation(LawId.L2, member, StripRedundantMemberPrefix(result.Description, member));
    }

    /// <summary>ArchUnitNET's own <c>Description</c> already starts with the offending member's full
    /// name (e.g. <c>"Foo does depend on \"Npgsql.NpgsqlConnection\""</c>) — <see cref="DependencyLawAssert.Format"/>
    /// prints <c>Member</c> right beside <c>Detail</c>, so keeping that prefix would repeat the same
    /// name twice in one failure line (STORY-291 review). Stripped only when it's actually there —
    /// ArchUnitNET's exact phrasing isn't a documented contract this rule should assume forever.</summary>
    private static string StripRedundantMemberPrefix(string description, string member)
    {
        var prefix = member + " ";
        return description.StartsWith(prefix, StringComparison.Ordinal) ? description[prefix.Length..] : description;
    }
}
