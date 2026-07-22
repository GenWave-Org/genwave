using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Scripted <see cref="IActivePersonaAccessor"/> double (STORY-121). <see cref="Persona"/> defaults to
/// null (the persona-less resolve every pre-T6 Orchestrator spec still exercises). Set
/// <see cref="ThrowOnResolve"/> to simulate an accessor fault — the real contract never throws
/// (F35.5), but the Orchestrator must stay defensive per F12.4 regardless.
///
/// <see cref="Card"/> (STORY-213, PLAN T64) is <see cref="ResolveCardAsync"/>'s own script —
/// independent of <see cref="Persona"/>, mirroring the real accessor's own two-independent-reads
/// shape (<c>GenWave.Host.Options.ActivePersonaAccessor</c>'s remarks) so a spec can exercise "active
/// persona, no card" without also faking a store fault.
/// </summary>
sealed class FakeActivePersonaAccessor : IActivePersonaAccessor
{
    public Persona? Persona { get; set; }
    public PersonaCard? Card { get; set; }
    public Exception? ThrowOnResolve { get; set; }

    public Task<Persona?> ResolveAsync(CancellationToken ct)
    {
        if (ThrowOnResolve is { } ex)
            throw ex;

        return Task.FromResult(Persona);
    }

    public Task<PersonaCard?> ResolveCardAsync(CancellationToken ct) => Task.FromResult(Card);
}
