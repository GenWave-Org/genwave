using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Scripted <see cref="IActivePersonaAccessor"/> double (STORY-121). <see cref="Persona"/> defaults to
/// null (the persona-less resolve every pre-T6 Orchestrator spec still exercises). Set
/// <see cref="ThrowOnResolve"/> to simulate an accessor fault — the real contract never throws
/// (F35.5), but the Orchestrator must stay defensive per F12.4 regardless.
/// </summary>
sealed class FakeActivePersonaAccessor : IActivePersonaAccessor
{
    public Persona? Persona { get; set; }
    public Exception? ThrowOnResolve { get; set; }

    public Task<Persona?> ResolveAsync(CancellationToken ct)
    {
        if (ThrowOnResolve is { } ex)
            throw ex;

        return Task.FromResult(Persona);
    }
}
