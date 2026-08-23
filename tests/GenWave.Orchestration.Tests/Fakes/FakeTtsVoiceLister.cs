using System.Net.Http;
using GenWave.Core.Abstractions;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Scripted <see cref="ITtsVoiceLister"/> double for orchestrator unit tests (STORY-358, PLAN T341) —
/// the announcement voice-validation seam (SPEC F144.2's "when known" clause). <see cref="KnownVoices"/>
/// stands in for the TTS backend's own installed voice ids; <see cref="Throw"/> simulates the registry
/// being unreachable (a live network call in production), which the Orchestrator must degrade past
/// rather than let fault the whole unit.
/// </summary>
sealed class FakeTtsVoiceLister : ITtsVoiceLister
{
    public IReadOnlyList<string> KnownVoices { get; set; } = [];
    public bool Throw { get; set; }

    public Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct) =>
        Throw
            ? throw new HttpRequestException("Simulated voice registry outage (test double).")
            : Task.FromResult(KnownVoices);
}
