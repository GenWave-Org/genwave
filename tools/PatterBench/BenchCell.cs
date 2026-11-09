namespace GenWave.PatterBench;

using GenWave.Core.Domain;

/// <summary>What one model wrote for one persona, and how long each call took.</summary>
internal sealed record BenchCell(
    string Model,
    string Persona,
    PersonaCard Card,
    IReadOnlyList<string> Copies,
    int GatePasses,
    IReadOnlyList<TimeSpan> Latencies,
    double? VramGiB);
