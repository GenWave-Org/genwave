using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IJinglePackStore"/> double (SPEC F165.2/F165.5/F165.6, STORY-399/401, PLAN
/// T414) — mirrors <see cref="FakeVoicePackStore"/>'s own upsert-by-slug contract, one asset kind
/// over: a new slug inserts a row per asset, keyed by title (mirrors the real
/// <c>library.media_pack_slug_title_key</c> upsert key), an existing title's row keeps its OWN id
/// across a reinstall (SPEC F165.5's "the id is preserved on re-install" contract) rather than being
/// dropped and recreated. This double proves the CONTRACT
/// <see cref="GenWave.Host.Api.JinglePackController"/>'s install route relies on over HTTP — the REAL
/// SQL, including <see cref="IJinglePackStore.UpsertAsync"/>'s own cross-schema
/// <c>station.ad_spot.bed_media_id</c> reference guard and its station-first-with-compensation
/// ordering, a fake dictionary cannot honestly repeat, is proven against real Postgres instead (see
/// <c>Story399_JinglePackInstall.cs</c>'s <c>JinglePackInstallArc</c>,
/// <c>Story401_PackUninstallGuards.cs</c>'s <c>JinglePackUninstallArc</c>, and
/// <c>Story399_JinglePackReinstallOrphans.cs</c>'s own two-instance arc). This fake exists only to
/// prove the CONTROLLER's own unwind-on-any-failure discipline cheaply — no ephemeral Postgres, no
/// real ads-library row — the same reasoning <see cref="FakeVoicePackStore"/>'s own remarks give.
/// </summary>
sealed class FakeJinglePackStore : IJinglePackStore
{
    sealed record InstalledPack(
        string Definition, string ImportedFrom,
        Dictionary<string, long> MediaIdsByTitle, Dictionary<string, string> PathsByTitle);

    readonly Dictionary<string, InstalledPack> bySlug = new(StringComparer.Ordinal);
    long nextMediaId = 1;

    /// <summary>Installed pack count — this fake's own stand-in for a listing method with no
    /// production caller (mirrors <see cref="FakeVoicePackStore.PackCount"/>).</summary>
    public int PackCount => bySlug.Count;

    /// <summary>Scripts the NEXT <see cref="UpsertAsync"/> call to THROW this instead of writing —
    /// mirrors <see cref="FakeVoicePackStore.ThrowOnUpsert"/>'s own idiom exactly, proving
    /// <see cref="GenWave.Host.Api.JinglePackController"/>'s own unwind-on-any-failure path
    /// (including a cancellation surfacing as <see cref="OperationCanceledException"/>) without a
    /// real Postgres failure. Cleared after one use.</summary>
    public Exception? ThrowOnUpsert { get; set; }

    public Task<IReadOnlyList<string>> FindPathsForPackAsync(string slug, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(
            bySlug.TryGetValue(slug, out var pack) ? pack.PathsByTitle.Values.ToList() : []);

    public Task<JinglePackUpsertResult> UpsertAsync(
        string slug, string definition, string importedFrom,
        IReadOnlyList<JinglePackAssetInput> assets, CancellationToken ct)
    {
        if (ThrowOnUpsert is { } toThrow)
        {
            ThrowOnUpsert = null;
            throw toThrow;
        }

        var previous = bySlug.TryGetValue(slug, out var existingPack) ? existingPack : null;

        var mediaIds = new List<long>(assets.Count);
        var mediaIdsByTitle = new Dictionary<string, long>(StringComparer.Ordinal);
        var pathsByTitle = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var asset in assets)
        {
            var title = asset.Media.Tags.Title;
            var id = previous is not null && previous.MediaIdsByTitle.TryGetValue(title, out var existingId)
                ? existingId
                : nextMediaId++;
            mediaIds.Add(id);
            mediaIdsByTitle[title] = id;
            pathsByTitle[title] = asset.Media.Path;
        }

        bySlug[slug] = new InstalledPack(definition, importedFrom, mediaIdsByTitle, pathsByTitle);
        return Task.FromResult<JinglePackUpsertResult>(new JinglePackUpsertResult.Upserted(mediaIds));
    }

    public Task<JinglePackDeleteResult> DeleteAsync(string slug, CancellationToken ct)
    {
        if (!bySlug.TryGetValue(slug, out var pack))
            return Task.FromResult<JinglePackDeleteResult>(new JinglePackDeleteResult.NotFound());

        bySlug.Remove(slug);
        return Task.FromResult<JinglePackDeleteResult>(new JinglePackDeleteResult.Deleted(pack.PathsByTitle.Values.ToList()));
    }

    /// <summary>Test-only accessor: the media id a successful install assigned to
    /// <paramref name="title"/> under <paramref name="slug"/>, or <see langword="null"/> if no such
    /// pack/title is installed.</summary>
    public long? TryGetMediaId(string slug, string title) =>
        bySlug.TryGetValue(slug, out var pack) && pack.MediaIdsByTitle.TryGetValue(title, out var id) ? id : null;

    public Task<IReadOnlyList<InstalledPackDefinition>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<InstalledPackDefinition>>(bySlug
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new InstalledPackDefinition(kv.Key, kv.Value.Definition))
            .ToList());
}
