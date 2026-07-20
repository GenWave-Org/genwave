namespace GenWave.Tts;

/// <summary>
/// A single dependency's health check (SPEC F70.2, STORY-187). <see cref="DependencyHealthProber"/>
/// holds every registered instance as a plain <c>IEnumerable&lt;IDependencyProbe&gt;</c> — adding a
/// dependency (T34's Piper probe) is exactly one more DI registration, no separate registry data
/// structure to maintain.
/// </summary>
public interface IDependencyProbe
{
    /// <summary>The <see cref="DependencyNames"/> key this probe's verdicts are recorded under.</summary>
    string DependencyName { get; }

    /// <summary>
    /// Runs one lightweight health check. Returns true when the dependency answered healthy,
    /// false when the dependency is deliberately unconfigured — the disabled-by-design state
    /// (e.g. an empty <c>Llm:Endpoint</c>, SPEC F34.2) rather than an actual failure. Throws for
    /// every other failure (timeout, connect failure, non-2xx response) — the caller
    /// (<see cref="DependencyHealthProber"/>) turns that into an unhealthy verdict carrying the
    /// failure reason (SPEC F70.2 AC3); this type never decides what that reason text is.
    /// </summary>
    Task<bool> ProbeAsync(CancellationToken ct);
}
