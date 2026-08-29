// Fixture type for T357's L2-narrowing self-exercising probe (Story290_DependencyLaws.cs,
// ScenarioL2PostgresConfinement). Never wired into any DI container or call path — namespaced
// GenWave.MediaLibrary.Garden (the REAL production namespace text PostgresConfinement.RepositoryLayer
// matches against) so the probe proves the narrowed law's actual behavior, not a namespace-text
// stand-in.

namespace GenWave.MediaLibrary.Garden;

/// <summary>Stands in for a real Gardener repository (e.g. MediaRotationRepository): Garden-namespaced
/// AND Repository-named, so PostgresConfinement.RepositoryLayer's narrowed Garden entry allows it to
/// touch Npgsql — the probe's "stays clean" half.</summary>
public sealed class CompliantGardenRepository
{
    public object Open(string connectionString) => new Npgsql.NpgsqlConnection(connectionString);
}
