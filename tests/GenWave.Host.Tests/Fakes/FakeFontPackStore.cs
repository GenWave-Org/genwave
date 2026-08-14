using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

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

    /// <summary>Content <see cref="GetFaceByFileAsync"/> serves, by file — <see cref="UpsertAsync"/>'s
    /// own bytes-carrying counterpart to <see cref="bySlug"/>'s metadata-only <see cref="FontPack.Faces"/>
    /// (mirrors <see cref="IFontPackStore.GetAllAsync"/>/<see cref="IFontPackStore.GetFaceByFileAsync"/>'s
    /// own real-repository split: one method lists metadata, the other serves one face's payload).
    /// <c>InstalledFontCatalog.ReloadAsync</c> (PLAN T200) is the first Fact-visible reader of this —
    /// before T200 this always returned <see langword="null"/> (see this class's former remarks).</summary>
    readonly Dictionary<string, FontPackFaceContent> contentByFile = new(StringComparer.Ordinal);

    /// <summary>Seeds already-"installed" packs, for a spec that needs one in place before the Fact's
    /// own install attempt (e.g. a cross-pack filename collision). Metadata only, mirroring
    /// <see cref="FontPack.Faces"/>'s own shape — a seeded pack carries no bytes for
    /// <see cref="GetFaceByFileAsync"/> to serve, which is fine for every seeding use so far (none of
    /// them exercises <c>/fonts/{file}</c> against a SEEDED face; <c>UpsertAsync</c> below is the one
    /// path that populates <see cref="contentByFile"/>).</summary>
    public FakeFontPackStore(params FontPack[] seeded)
    {
        foreach (var pack in seeded)
            bySlug[pack.Slug] = pack;
    }

    /// <summary>
    /// Seeds one already-"installed" pack WITH its bytes-carrying content, bypassing the
    /// install-route dance entirely — for a spec whose own concern is SERVING an installed face
    /// (<c>Story283_InstalledFontServing.cs</c>, PLAN T200), not installing one
    /// (<c>Story282_FontPackInstall.cs</c>'s own concern). Mirrors
    /// <c>Story278_ThemeCatalogIsolation.cs</c>'s own <c>BuildLiveThemeStoreAsync</c> precedent: write
    /// directly to the fake store rather than re-deriving a whole catalog-fetch fixture per serving
    /// spec.
    /// </summary>
    public static FakeFontPackStore WithInstalledFace(
        string slug, string family, string file, byte[] bytes, string sha256, string style = FontPackFaceInput.NormalStyle)
    {
        var store = new FakeFontPackStore();
        var face = new FontPackFace(file, style, bytes.Length, sha256);
        store.bySlug[slug] = new FontPack(slug, family, "{}", slug, DateTime.UtcNow, DateTime.UtcNow, [face]);
        store.contentByFile[file] = new FontPackFaceContent(bytes, sha256);
        return store;
    }

    /// <summary>When set, every read (<see cref="GetAllAsync"/>/<see cref="GetFaceByFileAsync"/>)
    /// throws — simulates "the DB is gone" for <c>InstalledFontCatalog</c>'s own SPEC F104.8
    /// offline-floor Facts (PLAN T200): a face already folded into <c>InstalledFontCatalog</c>'s
    /// snapshot BEFORE this flips must keep serving even though the store itself can no longer
    /// answer. <see cref="UpsertAsync"/> is deliberately unaffected — every outage Fact this exists
    /// for is about a READ-side failure post-load, never a write attempt.</summary>
    public bool Broken { get; set; }

    /// <summary>Scripts the NEXT <see cref="UpsertAsync"/> call to return this
    /// <see cref="FontPackUpsertResult"/> instead of writing — proves <c>FontPackController</c>'s own
    /// <see cref="FontPackUpsertResult.FileCollision"/>-to-409 mapping without a real Postgres fixture
    /// (gh-#406 slice 2: the store now RETURNS the collision case rather than throwing — the real
    /// 23505-to-<see cref="FontPackUpsertResult.FileCollision"/> mapping is
    /// <c>FontPackRepository.UpsertAsync</c>'s own concern, proven against real Postgres in
    /// <c>GenWave.MediaLibrary.Tests/Specs/Story282_FontPackRepository.cs</c>; this fake only proves
    /// the controller's own response-mapping over an already-resolved case, mirroring this class's own
    /// header remarks). Cleared after one use.</summary>
    public FontPackUpsertResult? NextUpsertResult { get; set; }

    /// <summary>The number of <see cref="UpsertAsync"/> calls that actually wrote (a scripted throw
    /// does not count) — lets a spec assert the install route reaches the store exactly once per
    /// install, the call-shape half of "one transaction, no partial installs" a fake store can
    /// honestly prove.</summary>
    public int UpsertCallCount { get; private set; }

    /// <summary>
    /// Scripts <see cref="DeleteAsync"/>'s own guard outcome (SPEC F104.14, STORY-288, PLAN T208):
    /// when non-empty, the NEXT <see cref="DeleteAsync"/> call for a slug this fake actually holds
    /// returns <see cref="FontPackDeleteResult.Referenced"/> naming these slugs instead of deleting
    /// anything. This fake deliberately carries NO knowledge of <c>station.theme</c> — mirrors this
    /// class's own header remarks ("proves the CONTRACT FontPackController's own route relies on; the
    /// real repository's own SQL is proven against real Postgres instead"): the REAL cross-table
    /// substring-search guard is <c>GenWave.MediaLibrary.Station.FontPackRepository.DeleteAsync</c>'s
    /// own concern, proven in <c>GenWave.MediaLibrary.Tests/Specs/Story288_FontPackUninstall.cs</c>; a
    /// spec against THIS fake sets this property directly to drive
    /// <c>FontPackController.Uninstall</c>'s own response-mapping through both branches over a real HTTP
    /// request.
    /// </summary>
    public IReadOnlyList<string> ReferencingThemeSlugs { get; set; } = [];

    public Task<FontPackUpsertResult> UpsertAsync(
        string slug, string family, string definition, string importedFrom,
        IReadOnlyList<FontPackFaceInput> faces, CancellationToken ct)
    {
        if (NextUpsertResult is { } scripted)
        {
            NextUpsertResult = null;
            return Task.FromResult(scripted);
        }

        UpsertCallCount++;
        var createdAt = bySlug.TryGetValue(slug, out var existing) ? existing.CreatedAt : DateTime.UtcNow;
        var storedFaces = faces.Select(f => new FontPackFace(f.File, f.Style, f.ByteSize, f.Sha256)).ToList();
        bySlug[slug] = new FontPack(slug, family, definition, importedFrom, DateTime.UtcNow, createdAt, storedFaces);

        foreach (var face in faces)
            contentByFile[face.File] = new FontPackFaceContent(face.Bytes, face.Sha256);

        return Task.FromResult<FontPackUpsertResult>(new FontPackUpsertResult.Upserted());
    }

    public Task<IReadOnlyList<FontPack>> GetAllAsync(CancellationToken ct)
    {
        if (Broken)
            throw new InvalidOperationException("simulated station.font_pack outage (FakeFontPackStore.Broken)");

        return Task.FromResult<IReadOnlyList<FontPack>>(bySlug.Values.ToList());
    }

    public Task<FontPackFaceContent?> GetFaceByFileAsync(string file, CancellationToken ct)
    {
        if (Broken)
            throw new InvalidOperationException("simulated station.font_pack_face outage (FakeFontPackStore.Broken)");

        return Task.FromResult(contentByFile.TryGetValue(file, out var content) ? content : null);
    }

    /// <summary>See <see cref="ReferencingThemeSlugs"/>'s own remarks — a slug this fake does not hold
    /// answers <see cref="FontPackDeleteResult.NotFound"/> regardless of that property, mirroring the
    /// real repository's own "does it exist at all" check.</summary>
    public Task<FontPackDeleteResult> DeleteAsync(string slug, CancellationToken ct)
    {
        if (!bySlug.TryGetValue(slug, out var pack))
            return Task.FromResult<FontPackDeleteResult>(new FontPackDeleteResult.NotFound());

        if (ReferencingThemeSlugs.Count > 0)
            return Task.FromResult<FontPackDeleteResult>(new FontPackDeleteResult.Referenced(ReferencingThemeSlugs));

        bySlug.Remove(slug);
        foreach (var face in pack.Faces)
            contentByFile.Remove(face.File);

        return Task.FromResult<FontPackDeleteResult>(new FontPackDeleteResult.Deleted());
    }
}
