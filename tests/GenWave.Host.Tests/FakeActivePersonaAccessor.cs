using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests;

/// <summary>
/// Scriptable <see cref="IActivePersonaAccessor"/> double shared across specs that need a controller
/// constructed directly (mirrors <see cref="FakeMediaCatalog"/>'s idiom). Defaults to "no active
/// persona" — set <see cref="Persona"/> to script a resolved one.
/// </summary>
sealed class FakeActivePersonaAccessor : IActivePersonaAccessor
{
    public Persona? Persona { get; set; }

    public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult(Persona);
}
