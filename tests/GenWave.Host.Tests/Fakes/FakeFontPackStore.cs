using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IFontPackStore"/> double (SPEC F104.5, STORY-282, PLAN T199) — mirrors
/// <see cref="FakeThemeStore"/>'s own upsert-by-slug contract (T181): a new slug inserts a row, an
/// existing one replaces its pack row AND its entire face set unconditionally
/// (<c>FontPackRepository</c>'s own delete-then-reinsert contract, T198, at the fake-store altitude).
/// This double proves the CONTRACT <c>FontPackController</c>'s install route relies on; the real
/// repository's own SQL — including the true no-partial-installs rollback a fake dictionary write
/// cannot honestly repeat — is proven against real Postgres in
/// <c>GenWave.MediaLibrary.Tests/Specs/Story282_FontPackRepository.cs</c> instead.
/// </summary>
sealed class FakeFontPackStore : IFontPackStore
{
    readonly Dictionary<string, FontPack> bySlug = new(StringComparer.Ordinal);

    /// <summary>Seeds already-"installed" packs, for a spec that needs one in place before the Fact's
    /// own install attempt (e.g. a cross-pack filename collision).</summary>
    public FakeFontPackStore(params FontPack[] seeded)
    {
        foreach (var pack in seeded)
            bySlug[pack.Slug] = pack;
    }

    /// <summary>Scripts the NEXT <see cref="UpsertAsync"/> call to throw this
    /// <see cref="PostgresException"/> instead of writing — proves
    /// <c>FontPackController</c>'s own 23505-to-409 mapping without a real Postgres fixture (mirrors
    /// <c>FakeScheduleStore.NextThrow</c>'s own precedent for <c>ScheduleController</c>'s
    /// PostgresException handling). Cleared after one use.</summary>
    public PostgresException? NextThrow { get; set; }

    /// <summary>The number of <see cref="UpsertAsync"/> calls that actually wrote (a scripted throw
    /// does not count) — lets a spec assert the install route reaches the store exactly once per
    /// install, the call-shape half of "one transaction, no partial installs" a fake store can
    /// honestly prove.</summary>
    public int UpsertCallCount { get; private set; }

    public Task UpsertAsync(
        string slug, string family, string definition, string importedFrom,
        IReadOnlyList<FontPackFaceInput> faces, CancellationToken ct)
    {
        if (NextThrow is { } ex)
        {
            NextThrow = null;
            throw ex;
        }

        UpsertCallCount++;
        var createdAt = bySlug.TryGetValue(slug, out var existing) ? existing.CreatedAt : DateTime.UtcNow;
        var storedFaces = faces.Select(f => new FontPackFace(f.File, f.Style, f.ByteSize, f.Sha256)).ToList();
        bySlug[slug] = new FontPack(slug, family, definition, importedFrom, DateTime.UtcNow, createdAt, storedFaces);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FontPack>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FontPack>>(bySlug.Values.ToList());

    // No Fact in this project reaches for a served face's bytes yet — that seam belongs to PLAN T200
    // (the widened /fonts/{file} route). A clean "nothing installed under this file" miss is enough
    // to keep this double honest without a payload no Fact ever inspects.
    public Task<FontPackFaceContent?> GetFaceByFileAsync(string file, CancellationToken ct) =>
        Task.FromResult<FontPackFaceContent?>(null);
}
