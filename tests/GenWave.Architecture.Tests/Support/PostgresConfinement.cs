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
    /// <summary>MediaLibrary's actual repository layer (discovered at T211 adoption): the two
    /// namespaces every <c>*Repository</c> class lives in and queries from.</summary>
    public static readonly IObjectProvider<IType> RepositoryLayer = Types().That()
        .ResideInNamespace("GenWave.MediaLibrary.Catalog")
        .Or().ResideInNamespace("GenWave.MediaLibrary.Station");

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
            .Select(r => new LawViolation(LawId.L2, r.EvaluatedObjectIdentifier.ToString() ?? "<unknown>", r.Description))
            .ToList();
    }
}
