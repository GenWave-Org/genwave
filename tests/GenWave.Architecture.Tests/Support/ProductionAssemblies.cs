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
}
