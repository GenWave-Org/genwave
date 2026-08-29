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
    /// <summary>MediaLibrary's actual repository layer: the namespaces every <c>*Repository</c>
    /// class lives in and queries from. Catalog/Station discovered at T211 adoption; Garden added
    /// at T355 (SPEC F149.1-F149.3, STORY-367, gh-#529) for <c>MediaRotationRepository</c> — the
    /// Library Gardener's own home per ARCHITECTURE.md, confined by this same law like every other
    /// repository namespace here.
    ///
    /// <para>
    /// T355 review LOW-3: this allowlist is NAMESPACE-scoped, not TYPE-scoped — a future
    /// non-repository type dropped into <c>Garden/</c> (e.g. a <c>GardenerService</c>) would open
    /// Dapper/Npgsql unnoticed, the same gap Catalog/Station have already carried since T211. Left
    /// as-is for T355; narrowing this law to match <c>*Repository</c> by NAME rather than namespace
    /// is T357/T372's own scope.
    /// </para></summary>
    public static readonly IObjectProvider<IType> RepositoryLayer = Types().That()
        .ResideInNamespace("GenWave.MediaLibrary.Catalog")
        .Or().ResideInNamespace("GenWave.MediaLibrary.Station")
        .Or().ResideInNamespace("GenWave.MediaLibrary.Garden");

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
