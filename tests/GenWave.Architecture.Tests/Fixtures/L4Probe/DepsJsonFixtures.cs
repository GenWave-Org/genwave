// Fixture data for STORY-290 AC5's self-exercising negative probe (Story290_DependencyLaws.cs).
// Synthetic .deps.json content — never read from disk, never wired into any build — so the L4
// half of the probe is decoupled from GenWave.Abstractions' real, live dependency graph (N1).

namespace GenWave.Architecture.Tests.Fixtures.L4Probe;

/// <summary>Minimal <c>.deps.json</c> "libraries" content shaped exactly the way
/// <see cref="GenWave.Architecture.Tests.Support.DepsJsonDependencyScan.ExtraLibraries"/> parses
/// it: a self-only closure (the clean case) and a self-plus-one-package closure (the case a real
/// <c>System.Diagnostics.EventLog</c>-style bypass would produce).</summary>
internal static class DepsJsonFixtures
{
    public const string SelfOnly = """
        { "libraries": { "Probe.Assembly/1.0.0": {} } }
        """;

    public const string SelfPlusExtraPackage = """
        { "libraries": { "Probe.Assembly/1.0.0": {}, "System.Diagnostics.EventLog/9.0.0": {} } }
        """;
}
