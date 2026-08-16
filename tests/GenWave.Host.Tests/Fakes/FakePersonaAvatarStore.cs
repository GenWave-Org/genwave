using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IPersonaAvatarStore"/> double — Story332's own "a worn copy survives an
/// avatar-pack uninstall" Fact uses this directly to seed a worn face BEFORE the uninstall (T290's own
/// store contract, arranged straight against the seam rather than a whole apply-from-pack write path
/// that does not exist yet, PLAN T295) and assert it untouched AFTER, proving
/// <c>AvatarPackController.Uninstall</c>'s own guard-free delete never reaches
/// <c>station.persona_avatar</c> at all (ARCHITECTURE.md's "assignment copies, provenance records"
/// ruling — see <see cref="AvatarPackController"/>'s own UNINSTALL IS GUARD-FREE remarks). Mirrors
/// <see cref="FakeFontPackStore"/>'s own minimal-contract idiom.
/// </summary>
sealed class FakePersonaAvatarStore : IPersonaAvatarStore
{
    readonly Dictionary<long, PersonaAvatar> byPersonaId = new();

    /// <summary>Bumped on every <see cref="GetByTokenAsync"/> call — Story335's own malformed-token
    /// facts use this to prove <c>SpectatorArtworkController.GetDjArtwork</c>'s well-formedness
    /// guard short-circuits BEFORE any lookup here, not merely happens to answer the same way. The
    /// real store is Postgres-backed (<c>PersonaAvatarRepository</c>): a malformed token must never
    /// buy a round trip against it.</summary>
    public int GetByTokenCallCount { get; private set; }

    public Task<PersonaAvatar?> GetByPersonaIdAsync(long personaId, CancellationToken ct) =>
        Task.FromResult(byPersonaId.TryGetValue(personaId, out var avatar) ? avatar : null);

    /// <summary>The in-memory answer to the token-only projection (PLAN T299 fix round) —
    /// <see cref="IPersonaAvatarStore.GetTokenByPersonaIdAsync"/>'s own contract, read straight off
    /// the same dictionary <see cref="GetByPersonaIdAsync"/> uses.</summary>
    public Task<string?> GetTokenByPersonaIdAsync(long personaId, CancellationToken ct) =>
        Task.FromResult(byPersonaId.TryGetValue(personaId, out var avatar) ? avatar.Token : null);

    public Task<PersonaAvatar?> GetByTokenAsync(string token, CancellationToken ct)
    {
        GetByTokenCallCount++;
        return Task.FromResult(byPersonaId.Values.FirstOrDefault(a => a.Token == token));
    }

    public Task UpsertAsync(PersonaAvatarInput avatar, CancellationToken ct)
    {
        byPersonaId[avatar.PersonaId] = new PersonaAvatar(
            avatar.PersonaId, avatar.Bytes, avatar.ByteSize, avatar.Sha256, avatar.Token,
            avatar.Source, avatar.ImportedFrom, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(long personaId, CancellationToken ct) =>
        Task.FromResult(byPersonaId.Remove(personaId));
}
