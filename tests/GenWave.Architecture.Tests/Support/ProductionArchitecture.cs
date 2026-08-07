using ArchUnitNET.Loader;
using ArchUnitArchitecture = ArchUnitNET.Domain.Architecture;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// The real production dependency graph, loaded once (ArchUnitNET's own guidance — loading is the
/// expensive part) and shared by every L2 fact in this suite. Includes Npgsql and Dapper
/// explicitly: <see cref="PostgresConfinement"/> needs both loaded, not merely referenced, for its
/// forbidden-assembly selector to see any targets at all.
///
/// <c>ArchUnitArchitecture</c> is an alias, not a style choice: this project's own namespace
/// (<c>GenWave.Architecture.Tests</c>) shadows the bare name <c>Architecture</c> that ArchUnitNET's
/// domain type otherwise uses.
/// </summary>
internal static class ProductionArchitecture
{
    public static readonly ArchUnitArchitecture Instance = new ArchLoader()
        .LoadAssemblies(
            ProductionAssemblies.Core,
            ProductionAssemblies.Orchestration,
            ProductionAssemblies.Tts,
            ProductionAssemblies.Loudness,
            ProductionAssemblies.MediaLibrary,
            ProductionAssemblies.Host,
            ProductionAssemblies.Abstractions,
            ProductionAssemblies.Npgsql,
            ProductionAssemblies.Dapper)
        .Build();
}
