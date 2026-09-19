namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Scripted <see cref="IActivePersonaAccessor"/> double (STORY-121). <see cref="Persona"/> defaults to
/// null (the persona-less resolve every pre-T6 spec still exercises); set it to simulate an active
/// persona reaching <see cref="LlmCopyWriter"/>'s prompt.
///
/// <see cref="Card"/> (STORY-193, F71.1/F71.3/F71.7) defaults to null too — every pre-F71 spec that
/// never sets it exercises the same legacy-fallback soul composition it always has (the interface's
/// own default <c>ResolveCardAsync</c> would already report "no card"; this override just makes the
/// card scriptable exactly like <see cref="Persona"/> is).
/// </summary>
public sealed class FakeActivePersonaAccessor : IActivePersonaAccessor
{
    public Persona? Persona { get; set; }
    public PersonaCard? Card { get; set; }

    /// <summary>
    /// When set, <see cref="ResolveCardAsync"/> throws this instead of returning <see cref="Card"/>
    /// (STORY-456 AC3) — simulates a faulting ambient persona-card store so a spec can prove a
    /// snapshot-driven render never reaches it.
    /// </summary>
    public Exception? ThrowOnResolve { get; set; }

    public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult(Persona);

    public Task<PersonaCard?> ResolveCardAsync(CancellationToken ct) =>
        ThrowOnResolve is { } ex ? Task.FromException<PersonaCard?>(ex) : Task.FromResult(Card);
}
