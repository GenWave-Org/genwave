// Fixture type for STORY-290 AC5's self-exercising negative probe (Story290_DependencyLaws.cs).
// Never wired into any DI container or call path — exists only so the probe can prove L2's
// ArchUnitNET-based detector actually discriminates a confined-layer type from an out-of-layer
// one, without editing production code or coupling the proof to production's violation count.

namespace GenWave.Architecture.Tests.Fixtures.L2Probe.RepositoryLike;

/// <summary>Stands in for a real MediaLibrary repository (Catalog/Station namespace) — the probe
/// excludes this namespace from "subjects" exactly the way the real L2 rule excludes
/// GenWave.MediaLibrary.Catalog/Station, so this type is allowed to touch Npgsql.</summary>
public sealed class CompliantRepository
{
    public object Open(string connectionString) => new Npgsql.NpgsqlConnection(connectionString);
}
