using System.Reflection;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// One anchor type per assembly this suite inspects, resolved via <c>typeof(Anchor).Assembly</c>
/// rather than <c>Assembly.Load("Name")</c> — a rename breaks the build here at compile time
/// instead of silently loading nothing at test time.
/// </summary>
internal static class ProductionAssemblies
{
    public static readonly Assembly Core = typeof(GenWave.Core.Abstractions.IScheduleStore).Assembly;
    public static readonly Assembly Orchestration = typeof(GenWave.Orchestration.PickResult).Assembly;
    public static readonly Assembly Tts = typeof(GenWave.Tts.DependencyNames).Assembly;
    public static readonly Assembly Loudness = typeof(GenWave.Loudness.CueDetectionOptions).Assembly;
    public static readonly Assembly MediaLibrary = typeof(GenWave.MediaLibrary.MediaLibraryServiceCollectionExtensions).Assembly;
    public static readonly Assembly Host = typeof(GenWave.Host.Api.BulkRatingController).Assembly;
    public static readonly Assembly Abstractions = typeof(GenWave.Abstractions.Playout.EnergyRange).Assembly;
    public static readonly Assembly Npgsql = typeof(global::Npgsql.NpgsqlConnection).Assembly;
    public static readonly Assembly Dapper = typeof(global::Dapper.SqlMapper).Assembly;

    /// <summary>L1's subjects: the framework-free inner projects, each with a stable label for
    /// failure messages (independent of runtime-derived assembly names).</summary>
    public static readonly IReadOnlyList<(string Label, Assembly Assembly)> InnerProjects = new[]
    {
        ("GenWave.Core", Core),
        ("GenWave.Orchestration", Orchestration),
        ("GenWave.Tts", Tts),
        ("GenWave.Loudness", Loudness),
    };

    /// <summary>Every GenWave production assembly, once (STORY-291 review carry-forward): L2 and L3
    /// each independently built this same seven-assembly list inline — a genuine copy, not two
    /// different lists that happen to look alike, so a project F105.4 adds later could go missing
    /// from one law's subjects and stay silently unchecked. A method, not a static-readonly field:
    /// L2 folds the result into an ArchUnitNET <c>GivenTypesConjunction</c>, whose fluent builders
    /// mutate the object each <c>.Or()</c> call chains onto (this file's own <see cref="InnerProjects"/>
    /// precedent and <see cref="HttpClientSeams"/>'s remarks both describe the same hazard) — sharing
    /// one cached starting point across facts would risk one fact's chain silently widening another's.
    /// Returning a fresh array every call costs nothing here and closes that door by construction.
    /// L3 (a plain <c>Assembly.Location</c> file-path scan, no ArchUnitNET involved) has no such
    /// hazard but takes the same list for the one reason this exists at all: one list, not
    /// two.</summary>
    public static IReadOnlyList<Assembly> AllProductionAssemblies() => new[]
    {
        Core, Orchestration, Tts, Loudness, MediaLibrary, Host, Abstractions,
    };

    /// <summary>Whether <paramref name="name"/> resolves to something real in
    /// <see cref="AllProductionAssemblies"/> at ANY of the three granularities a
    /// <see cref="LawViolation.Member"/>/<see cref="ArchitectureExemption.Member"/> string is ever
    /// written at today: a type's full name (e.g. <c>"GenWave.Host.Api.FontPackController"</c> — L1,
    /// L2, L3, L5's own shape, a plain type name), a member's <c>Type.Member</c> name (e.g.
    /// <c>"GenWave.Abstractions.Playout.EnergyRange.Min"</c> — L4-immutability's shape, T213: a
    /// settable property or mutable field), or a bare production assembly's simple name (e.g.
    /// <c>"GenWave.Abstractions"</c> — L4-references'/L6's shape when the offender IS the assembly
    /// itself, not one of its types). Reflection-based throughout, so it resolves <c>internal</c>
    /// members too (no <c>InternalsVisibleTo</c> needed) — the same reason
    /// <see cref="HttpClientSeams.DesignatedSeams"/> and <see cref="ExemptionBaseline"/> both name
    /// members as plain strings instead of <c>typeof</c>/<c>nameof</c>. The shared "every named member
    /// resolves" check (STORY-291 review, widened at T214/STORY-292's own resolution-fact review — L5
    /// itself only ever emits a plain type name, never a member; the wider granularities existed
    /// already via L4-immutability's and L4-references'/L6's shapes, just never previously exercised
    /// through this one shared mechanism): a deleted or typo'd entry in any exemption/seam/reservation
    /// list otherwise matches nothing and silently stops meaning anything.</summary>
    public static bool HasType(string name) =>
        AllProductionAssemblies().Any(assembly => assembly.GetType(name) is not null)
        || AllProductionAssemblies().Any(assembly => assembly.GetName().Name == name)
        || HasMember(name);

    /// <summary>Splits <paramref name="typeDotMember"/> on its LAST <c>.</c> — the boundary between a
    /// dotted type full name (itself containing dots for its namespace) and the one member name after
    /// it — and checks whether the left side resolves to a real production type that DECLARES (not
    /// inherits — <c>BindingFlags.DeclaredOnly</c>, closing the blind spot where e.g.
    /// <c>"GenWave.Abstractions.Playout.EnergyRange.ToString"</c> would otherwise resolve via
    /// <c>object.ToString</c>, meaning nothing about this type at all) a member (property, field,
    /// method, or nested type — <c>Type.GetMember</c> covers all of them) named the right side. Public
    /// or not: this answers "does this name mean something", not "is it a public API".</summary>
    private static bool HasMember(string typeDotMember)
    {
        var lastDot = typeDotMember.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == typeDotMember.Length - 1)
            return false;

        var candidateTypeName = typeDotMember[..lastDot];
        var candidateMemberName = typeDotMember[(lastDot + 1)..];

        var declaringType = AllProductionAssemblies()
            .Select(assembly => assembly.GetType(candidateTypeName))
            .FirstOrDefault(type => type is not null);

        return declaringType is not null
            && declaringType.GetMember(
                candidateMemberName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Length > 0;
    }
}
