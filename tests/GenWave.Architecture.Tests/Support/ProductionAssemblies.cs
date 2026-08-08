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

    /// <summary>Whether any <see cref="AllProductionAssemblies"/> assembly defines a type named
    /// <paramref name="fullName"/> — reflection-based, so it resolves <c>internal</c> types too (no
    /// <c>InternalsVisibleTo</c> needed), the same reason <see cref="HttpClientSeams.DesignatedSeams"/>
    /// and <see cref="ExemptionBaseline"/> both name members as plain strings instead of
    /// <c>typeof</c>. The shared "every named member resolves" check (STORY-291 review): a deleted or
    /// typo'd entry in either list otherwise matches nothing and silently stops meaning
    /// anything.</summary>
    public static bool HasType(string fullName) =>
        AllProductionAssemblies().Any(assembly => assembly.GetType(fullName) is not null);
}
