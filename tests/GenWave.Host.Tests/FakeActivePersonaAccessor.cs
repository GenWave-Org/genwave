using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests;

/// <summary>
/// Scriptable <see cref="IActivePersonaAccessor"/> double shared across specs that need a controller
/// constructed directly (mirrors <see cref="FakeMediaCatalog"/>'s idiom). Defaults to "no active
/// persona" — set <see cref="Persona"/> to script a resolved one.
/// </summary>
/// <remarks>
/// <see cref="Names"/> scripts <see cref="TryGetCachedName"/> (SPEC F93.1/F93.4, STORY-244, PLAN
/// T125) — the DB-free display-name memo the spectator now-playing poll reads. Empty by default (no
/// name known for any id), matching the real accessor's own cold-start answer; a spec asserting a
/// resolved <c>dj</c> maps the persona id it seeded onto the schedule/resolver fakes to a name here.
/// <see cref="ActivePersonaId"/> scripts the synchronous, zero-I/O on-air id read (SPEC F84.6,
/// STORY-215, PLAN T300) — <see langword="null"/> by default (the real accessor's own cold-start
/// answer); <c>GenWave.Host.Engine.ArtworkUrlResolver</c> reads this as its candidate persona id
/// before confirming agreement against <see cref="Names"/>.
/// </remarks>
sealed class FakeActivePersonaAccessor : IActivePersonaAccessor
{
    public Persona? Persona { get; set; }
    public IDictionary<long, string> Names { get; } = new Dictionary<long, string>();
    public long? ActivePersonaId { get; set; }

    public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult(Persona);

    public string? TryGetCachedName(long personaId) =>
        Names.TryGetValue(personaId, out var name) ? name : null;
}
