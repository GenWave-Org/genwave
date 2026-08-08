// Fixture type for STORY-290 AC5's self-exercising negative probe (Story290_DependencyLaws.cs).
// Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.L2Probe.Elsewhere;

/// <summary>Outside the confined namespace and touches Npgsql — the one type the probe expects to
/// fail.</summary>
public sealed class ViolatesConfinement
{
    public object Open(string connectionString) => new Npgsql.NpgsqlConnection(connectionString);
}
