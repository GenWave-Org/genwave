namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Scripted <see cref="IActivePersonaAccessor"/> double (STORY-121). <see cref="Persona"/> defaults to
/// null (the persona-less resolve every pre-T6 spec still exercises); set it to simulate an active
/// persona reaching <see cref="LlmCopyWriter"/>'s prompt.
/// </summary>
public sealed class FakeActivePersonaAccessor : IActivePersonaAccessor
{
    public Persona? Persona { get; set; }

    public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult(Persona);
}
