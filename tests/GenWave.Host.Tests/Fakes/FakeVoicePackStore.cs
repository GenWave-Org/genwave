using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IVoicePackStore"/> double (SPEC F164.5/F166.4, STORY-395/396/398, PLAN T413)
/// — mirrors <see cref="FakeFontPackStore"/>'s own upsert-by-slug contract: a new slug inserts a row,
/// an existing one replaces its voice roster outright (<c>VoicePackRepository</c>'s own
/// delete-then-reinsert contract, at the fake-store altitude). This double proves the CONTRACT
/// <see cref="GenWave.Host.Api.VoicePackController"/>'s install/uninstall routes rely on over HTTP;
/// the REAL SQL — including <see cref="IVoicePackStore.DeleteAsync"/>'s own cross-table
/// ad_spot/persona reference guard, which a fake dictionary cannot honestly repeat — is proven
/// against real Postgres in <c>Story401_PackUninstallGuards.cs</c> instead (that file uses
/// <c>EphemeralStationDatabase</c> + the real <c>VoicePackRepository</c>, never this fake).
///
/// <para>
/// Deliberately narrower than <see cref="FakeFontPackStore"/>, matching
/// <see cref="IVoicePackStore"/>'s own narrower seam (no <c>GetAllAsync</c> — see that interface's
/// own YAGNI remarks): this fake exposes test-only accessors (<see cref="PackCount"/>,
/// <see cref="TryGetVoices"/>) in place of a listing method with no production caller.
/// <see cref="DeleteAsync"/> here never refuses — every Story395/396/398 Fact against this fake only
/// ever exercises install, never the reference guard.
/// </para>
/// </summary>
sealed class FakeVoicePackStore : IVoicePackStore
{
    sealed record InstalledPack(string Engine, string Definition, string ImportedFrom, IReadOnlyList<VoicePackVoiceInput> Voices);

    readonly Dictionary<string, InstalledPack> bySlug = new(StringComparer.Ordinal);

    /// <summary>Scripts the NEXT <see cref="UpsertAsync"/> call to return this result instead of
    /// writing — mirrors <see cref="FakeFontPackStore.NextUpsertResult"/>'s own idiom, proving
    /// <see cref="GenWave.Host.Api.VoicePackController"/>'s own race-collision-to-409 mapping
    /// (<see cref="VoicePackUpsertResult.VoiceIdCollision"/>) without a real Postgres 23505. Cleared
    /// after one use.</summary>
    public VoicePackUpsertResult? NextUpsertResult { get; set; }

    /// <summary>The number of <see cref="UpsertAsync"/> calls that actually wrote (a scripted result
    /// does not count) — mirrors <see cref="FakeFontPackStore.UpsertCallCount"/>.</summary>
    public int UpsertCallCount { get; private set; }

    /// <summary>Installed pack count — this fake's own stand-in for the missing <c>GetAllAsync</c>
    /// (see this class's own remarks).</summary>
    public int PackCount => bySlug.Count;

    public Task<IReadOnlyList<VoicePackVoiceOwner>> FindInstalledVoiceIdsAsync(
        IReadOnlyList<string> voiceIds, string excludingSlug, CancellationToken ct)
    {
        var wanted = new HashSet<string>(voiceIds, StringComparer.Ordinal);
        var owners = new List<VoicePackVoiceOwner>();
        foreach (var (slug, pack) in bySlug)
        {
            if (string.Equals(slug, excludingSlug, StringComparison.Ordinal))
                continue;

            foreach (var voice in pack.Voices)
                if (wanted.Contains(voice.VoiceId))
                    owners.Add(new VoicePackVoiceOwner(voice.VoiceId, slug));
        }

        return Task.FromResult<IReadOnlyList<VoicePackVoiceOwner>>(owners);
    }

    public Task<IReadOnlyList<string>> FindVoiceIdsForPackAsync(string slug, CancellationToken ct)
    {
        IReadOnlyList<string> ids = bySlug.TryGetValue(slug, out var pack)
            ? pack.Voices.Select(voice => voice.VoiceId).ToList()
            : [];
        return Task.FromResult(ids);
    }

    /// <summary>Scripts the NEXT <see cref="UpsertAsync"/> call to THROW this instead of writing or
    /// returning — proves <see cref="GenWave.Host.Api.VoicePackController"/>'s own unwind-on-any-failure
    /// path (PLAN T413 review round 1 finding F2): a real Postgres 23514/23502/timeout/deadlock all
    /// propagate past <c>VoicePackRepository.UpsertAsync</c>'s own narrow 23505 catch exactly like this
    /// does. Cleared after one use, mirroring <see cref="NextUpsertResult"/>'s own idiom.</summary>
    public Exception? ThrowOnUpsert { get; set; }

    public Task<VoicePackUpsertResult> UpsertAsync(
        string slug, string engine, string definition, string importedFrom,
        IReadOnlyList<VoicePackVoiceInput> voices, CancellationToken ct)
    {
        if (ThrowOnUpsert is { } toThrow)
        {
            ThrowOnUpsert = null;
            throw toThrow;
        }

        if (NextUpsertResult is { } scripted)
        {
            NextUpsertResult = null;
            return Task.FromResult(scripted);
        }

        UpsertCallCount++;
        bySlug[slug] = new InstalledPack(engine, definition, importedFrom, voices.ToList());
        return Task.FromResult<VoicePackUpsertResult>(new VoicePackUpsertResult.Upserted());
    }

    public Task<VoicePackDeleteResult> DeleteAsync(string slug, CancellationToken ct)
    {
        if (!bySlug.TryGetValue(slug, out var pack))
            return Task.FromResult<VoicePackDeleteResult>(new VoicePackDeleteResult.NotFound());

        bySlug.Remove(slug);
        return Task.FromResult<VoicePackDeleteResult>(new VoicePackDeleteResult.Deleted(pack.Voices.Select(v => v.File).ToList()));
    }

    /// <summary>Test-only accessor: the voice inputs a successful install wrote for
    /// <paramref name="slug"/>, or <see langword="null"/> if no such pack is installed.</summary>
    public IReadOnlyList<VoicePackVoiceInput>? TryGetVoices(string slug) =>
        bySlug.TryGetValue(slug, out var pack) ? pack.Voices : null;
}
