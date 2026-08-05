namespace GenWave.Host.Catalog;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using GenWave.Host.Options;

/// <summary>
/// One guarded door to the community catalog shelf (STORY-234; SPEC F90.2-F90.4, generalised to
/// multiple kinds by F103): fetches, hash-verifies, and caches index.json plus individual entry
/// manifest/meta documents from the single
/// operator-configured origin (<see cref="CommunityCatalogAccessor.IndexUrl"/>). T101 builds the two
/// public endpoints on top of this seam; this type ships no endpoint of its own.
///
/// <para>
/// THE SSRF RULING (security-api review, T100): <see cref="CommunityCatalogAccessor.IndexUrl"/> is
/// an OPERATOR-controlled setting, trusted exactly like <c>Llm:Endpoint</c>/<c>Tts:Endpoint</c> — an
/// admin can already point either of those anywhere, and this codebase's whole trust model is
/// single-role (whoever can write station settings, Settings-policy gated, already has full run of
/// the process). Re-litigating that as a per-URL allowlist/DNS-rebind check here would be security
/// theater over an already-trusted boundary. The boundary that DOES matter, and that this class
/// enforces: REMOTE content — the index.json body itself — never gets to choose a fetch target.
/// Every entry path is validated by <see cref="CatalogIndexValidator"/> (regex shape + a second,
/// independent "resolves under the index directory" check) BEFORE it is ever turned into a URI, and
/// an index that fails ANY of that is rejected WHOLESALE with one WARN naming the offending value
/// (F90.2) — never partially trusted. No redirects are ever followed: the <see cref="HttpClientName"/>
/// client this service resolves has <c>AllowAutoRedirect = false</c> (Program.cs), so a 3xx response
/// is just another non-2xx status — just another fetch failure, never a hop this process takes.
/// </para>
///
/// <para>
/// CACHE (SPEC F90.4): one in-memory slot for the index, plus one slot per fetched entry slug (capped
/// at <see cref="MaxCachedEntries"/>, oldest-fetched-at evicted first), 15-minute TTL,
/// <see cref="TimeProvider"/>-injected (gh-#106 — never wall-clock read directly). On an
/// upstream/network failure the LAST-KNOWN-GOOD cached copy is served with its ORIGINAL fetched-at
/// timestamp; a cold cache with no prior success is the distinct "unreachable" outcome T101 maps to
/// a graceful empty state. A content-integrity failure (hash mismatch, oversize) is never served
/// from a stale copy either — those withhold the entry outright, regardless of what was cached
/// before. An index refresh only invalidates the entries it actually changed (a slug whose
/// manifest/meta sha256 is unchanged keeps its own cached content and stale-on-failure eligibility) —
/// see <see cref="PruneChangedEntries"/>.
/// </para>
///
/// <para>
/// SINGLE GLOBAL GATE, DELIBERATELY (doc fix, review finding): <see cref="singleFlight"/> is ONE
/// <see cref="SemaphoreSlim"/> for the WHOLE catalog surface, not one per resource — a concurrent
/// index refresh and an unrelated entry's manifest/meta fetch queue behind EACH OTHER, not just behind
/// their own resource's in-flight work (true head-of-line blocking, not just "two callers for the
/// SAME resource share one fetch"). Kept anyway: this is an admin-only, low-traffic surface (the
/// whole reason F90.4 asked for a 15-minute cache in the first place — it is not sized for
/// concurrent-listener-scale traffic), so the worst case is a handful of admin requests waiting a
/// few extra seconds behind one slow upstream call, never a user-facing stall. A per-slug lock
/// table would trade that bounded, rare cost for real complexity (lock lifecycle/cleanup, unbounded
/// growth) this surface's actual traffic never justifies (YAGNI).
/// </para>
/// </summary>
public sealed class CatalogProxyService(
    IHttpClientFactory httpClientFactory,
    CommunityCatalogAccessor catalogAccessor,
    TimeProvider timeProvider,
    ILogger<CatalogProxyService> logger)
{
    /// <summary>
    /// Name of the <see cref="IHttpClientFactory"/> client this service resolves (registered in
    /// Program.cs). A NAMED client resolved per call, not a typed <see cref="HttpClient"/>
    /// constructor parameter (contrast <c>MusicBrainzYearLookup</c>/the health probes): those are
    /// stateless and fine as <c>AddHttpClient&lt;T&gt;()</c> transients, but this service's cache
    /// and single-flight gate (SPEC F90.4) only work if it is the SAME instance across every
    /// request — the same "constructor-injected <see cref="IHttpClientFactory"/> + plain
    /// <c>AddSingleton&lt;T&gt;()</c>" shape <c>LlmCopyWriter</c> already uses for the same reason.
    /// </summary>
    public const string HttpClientName = "CatalogProxy";

    /// <summary>Size cap while reading index.json (SPEC F90.3) — enforced DURING the read, never buffered unbounded first.</summary>
    public const int MaxIndexBytes = 1024 * 1024;

    /// <summary>
    /// Size cap while reading an entry's manifest document (SPEC F90.3, matches F89.2's own
    /// build-time cap for a persona's <c>&lt;slug&gt;.persona.json</c> card) — kept as one constant
    /// name for the transport's own history, but F103.2 generalises what it caps to ANY kind's
    /// primary file (e.g. a theme's <c>&lt;slug&gt;.theme.json</c>); the size cap itself stays kind-agnostic.
    /// </summary>
    public const int MaxCardBytes = 256 * 1024;

    /// <summary>Size cap while reading a <c>&lt;slug&gt;.meta.json</c> document (SPEC F90.3, matches F89.2's own build-time cap).</summary>
    public const int MaxMetaBytes = 64 * 1024;

    /// <summary>
    /// Size cap while streaming ONE binary asset — a font pack's woff2 face or its OFL licence text
    /// (SPEC F104.1, T194) — enforced DURING the read by <see cref="CatalogHttpFetcher"/>'s own
    /// bounded stream, the exact same mechanism <see cref="MaxCardBytes"/>/<see cref="MaxMetaBytes"/>
    /// already use above. The cap actually PASSED to that read is
    /// <c>min(the asset's own declared <see cref="CatalogAssetRef.Bytes"/>, this constant)</c> — see
    /// <see cref="FetchAndVerifyAssetAsync"/> — never the origin's declared size alone (T193 review
    /// obligation): a stream that keeps sending bytes past what index.json itself claimed must still
    /// be cut off by a bound THIS process chose.
    ///
    /// <para>
    /// 256 KiB (262,144 bytes): FONTS.md/SPEC F104.2 caps a whole PACK's SUMMED asset bytes at 200
    /// KiB (204,800 bytes, catalog CI's own gate, T195 — not yet built in this app) — but that
    /// ceiling bounds the pack's TOTAL, while this constant bounds ONE FILE at a time. A two-asset
    /// pack sitting right at the 200 KiB pack ceiling could legally be one ~198 KiB face plus a few
    /// KiB of OFL.txt, so a per-asset cap set AT 200 KiB would reject that single face on a rounding
    /// technicality despite the whole pack being CI-approved. 256 KiB reuses <see cref="MaxCardBytes"/>'s
    /// own already-established magnitude (one more headroom-over-a-real-ceiling constant, not a new
    /// order of magnitude invented just for this cap) — comfortable headroom for any one file a
    /// 200 KiB-ceilinged pack could ever declare.
    /// </para>
    /// </summary>
    public const int MaxAssetBytes = 256 * 1024;

    /// <summary>
    /// Ceiling on <see cref="cachedAssets"/>' distinct entries — mirrors <see cref="MaxCachedEntries"/>'s
    /// own bounded-growth rationale, set far lower because each slot here can hold up to
    /// <see cref="MaxAssetBytes"/> (256 KiB) of raw bytes rather than a small JSON document: 64 slots
    /// is a 16 MiB worst case, comfortably bounded for an admin-only, low-traffic specimen-preview
    /// surface (SPEC F104.4) with headroom well beyond any one real pack's face count.
    /// </summary>
    const int MaxCachedAssets = 64;

    /// <summary>
    /// Ceiling on the number of distinct slugs' content held in <see cref="cachedEntries"/> at once
    /// (review finding — an unbounded per-slug cache is an admin-controlled but still unbounded
    /// growth vector: nothing stops a large upstream catalog, or a churn of slugs over many index
    /// refreshes, from growing this dictionary forever). Oldest-fetched-at is evicted first once
    /// this is exceeded — see <see cref="EvictOldestIfOverCapacity"/> — a plain size cap, not a
    /// true LRU (nothing here tracks LAST ACCESS, only last FETCH), which is enough for a shelf this
    /// small: 256 is generously above any real catalog's entry count today (F89.4's ~12-entry launch
    /// gate) with headroom for years of growth.
    /// </summary>
    public const int MaxCachedEntries = 256;

    static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    readonly object cacheGate = new();
    readonly SemaphoreSlim singleFlight = new(1, 1);
    CachedIndex? cachedIndex;
    readonly Dictionary<string, CachedEntry> cachedEntries = new(StringComparer.Ordinal);

    /// <summary>
    /// One slot per fetched asset (SPEC F104.1, T194), keyed by the asset's own
    /// <see cref="CatalogAssetRef.Path"/> (already globally unique — it embeds the owning slug).
    /// Same house cache posture as <see cref="cachedEntries"/>: <see cref="CacheTtl"/> parity with
    /// entries (no separate TTL invented for assets — a specimen preview and its pack's own
    /// manifest/meta are the SAME "how stale is admin-only catalog content allowed to be" question,
    /// F90.4), guarded by the SAME <see cref="singleFlight"/> gate (this type's own class remarks on
    /// why one gate covers the whole surface), and pruned on index refresh by
    /// <see cref="PruneChangedAssets"/> exactly the way <see cref="PruneChangedEntries"/> already
    /// prunes <see cref="cachedEntries"/>.
    /// </summary>
    readonly Dictionary<string, CachedAsset> cachedAssets = new(StringComparer.Ordinal);

    /// <summary>Test seam only (SPEC F90.4's <see cref="MaxCachedEntries"/> cap) — the number of entries currently cached, so a spec can assert the eviction cap without depending on wall-clock LRU timing.</summary>
    internal int CachedEntryCountForTests
    {
        get { lock (cacheGate) { return cachedEntries.Count; } }
    }

    /// <summary>GET the catalog shelf listing (SPEC F90.2). See this type's own remarks for the cache/stale/single-flight contract.</summary>
    public async Task<CatalogIndexFetchResult> GetIndexAsync(CancellationToken ct)
    {
        if (catalogAccessor.IndexUrl is not { } url)
            return new CatalogIndexFetchResult.Unreachable();

        if (TryServeFreshIndex(url, out var fresh))
            return fresh;

        // Review finding: Community:CatalogIndexUrl is validated by SettingValidator on every write
        // through the settings API, but an env/compose-only override bypasses that validator
        // entirely (ValidateDataAnnotations on CommunityOptions asserts nothing about this field) —
        // a garbage or non-http(s) value must degrade to Unreachable with one WARN here, never throw
        // a UriFormatException straight out of this method.
        if (!TryParseIndexUri(url, out var indexUri))
        {
            logger.LogWarning("Persona catalog index rejected: '{Url}' is not an absolute http/https URL", LogSafeText.Sanitize(url));
            return new CatalogIndexFetchResult.Unreachable();
        }

        await singleFlight.WaitAsync(ct);
        try
        {
            // Re-check: the single-flight winner (this call, or a sibling that got here first) may
            // already have refreshed the cache while this call was queued on the gate.
            if (TryServeFreshIndex(url, out var freshAfterWait))
                return freshAfterWait;

            var directory = ResolveDirectory(indexUri);
            var validated = await FetchAndValidateIndexAsync(indexUri, directory, ct);
            if (validated is { } entries)
            {
                var fetchedAt = timeProvider.GetUtcNow();
                lock (cacheGate)
                {
                    cachedIndex = new CachedIndex(url, directory, entries, fetchedAt);
                    // Only drops a cached entry the new index actually changed underneath (review
                    // finding) — see PruneChangedEntries's own remarks for why a blanket Clear()
                    // here was wrong.
                    PruneChangedEntries(entries);
                    PruneChangedAssets(entries);
                }

                return new CatalogIndexFetchResult.Ok(entries, fetchedAt);
            }

            // Stale-on-failure (F90.4): the same origin's last-known-good index beats nothing,
            // regardless of how long ago it was fetched.
            lock (cacheGate)
            {
                if (cachedIndex is { } stale && stale.SourceUrl == url)
                    return new CatalogIndexFetchResult.Ok(stale.Entries, stale.FetchedAt);
            }

            return new CatalogIndexFetchResult.Unreachable();
        }
        finally
        {
            singleFlight.Release();
        }
    }

    /// <summary>
    /// An absolute http/https URL, or nothing (SPEC F90.1's own validated shape — re-checked here
    /// rather than trusted, see <see cref="GetIndexAsync"/>'s own remarks on why).
    /// </summary>
    static bool TryParseIndexUri(string url, [NotNullWhen(true)] out Uri? uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme is "http" or "https")
            return true;

        uri = null;
        return false;
    }

    /// <summary>GET one entry's hash-verified manifest + meta content (SPEC F90.2, F90.3). Resolves the index first — see <see cref="GetIndexAsync"/>.</summary>
    public async Task<CatalogEntryFetchResult> GetEntryAsync(string slug, CancellationToken ct)
    {
        var indexResult = await GetIndexAsync(ct);
        if (indexResult is not CatalogIndexFetchResult.Ok)
            return new CatalogEntryFetchResult.Unreachable();

        if (!TryResolveSummary(slug, out var summary, out var directory))
            return new CatalogEntryFetchResult.NotFound();

        if (TryServeFreshEntry(slug, out var fresh))
            return fresh;

        await singleFlight.WaitAsync(ct);
        try
        {
            if (TryServeFreshEntry(slug, out var freshAfterWait))
                return freshAfterWait;

            var outcome = await FetchAndVerifyEntryAsync(summary, directory, ct);
            return outcome switch
            {
                EntryFetchOutcome.Ok ok => CacheAndReturnEntry(slug, summary, ok),
                EntryFetchOutcome.HashMismatch mismatch => WithheldHashMismatch(slug, mismatch),
                EntryFetchOutcome.Oversize oversize => WithheldOversize(slug, oversize),
                EntryFetchOutcome.NetworkFailure => ServeStaleEntryOrUnreachable(slug),
                // EntryFetchOutcome's constructor is private (closed hierarchy) — this arm can
                // never actually run; it exists only because Roslyn's pattern-exhaustiveness
                // checker doesn't treat a private-constructor closed hierarchy as provably
                // exhaustive (mirrors PersonaController.Import's own defensive discard arm).
                _ => throw new UnreachableException($"Unhandled {nameof(EntryFetchOutcome)} case."),
            };
        }
        finally
        {
            singleFlight.Release();
        }
    }

    /// <summary>
    /// GET one binary asset's hash-verified bytes (SPEC F104.1, F104.4, T194) — a font pack's woff2
    /// face or its OFL licence text. Resolves the index first (same as <see cref="GetEntryAsync"/>),
    /// then matches <paramref name="file"/> against the entry's own already-validated
    /// <see cref="CatalogEntrySummary.Assets"/> BY BARE FILENAME — never by re-deriving a path from
    /// caller input (mirrors <c>FontEndpoints</c>' own "compared for equality, never concatenated
    /// into a path" posture): a <paramref name="file"/> naming nothing on the resolved entry's own
    /// asset list is <see cref="CatalogAssetFetchResult.NotFound"/>, exactly like an unknown slug.
    /// Same size-cap/hash-verify/single-flight/cache contract as <see cref="GetEntryAsync"/> — see
    /// <see cref="MaxAssetBytes"/> and <see cref="cachedAssets"/>'s own remarks.
    /// </summary>
    public async Task<CatalogAssetFetchResult> GetAssetAsync(string slug, string file, CancellationToken ct)
    {
        var indexResult = await GetIndexAsync(ct);
        if (indexResult is not CatalogIndexFetchResult.Ok)
            return new CatalogAssetFetchResult.Unreachable();

        if (!TryResolveAsset(slug, file, out var assetRef, out var directory))
            return new CatalogAssetFetchResult.NotFound();

        if (TryServeFreshAsset(assetRef.Path, out var fresh))
            return fresh;

        await singleFlight.WaitAsync(ct);
        try
        {
            if (TryServeFreshAsset(assetRef.Path, out var freshAfterWait))
                return freshAfterWait;

            var outcome = await FetchAndVerifyAssetAsync(directory, assetRef, ct);
            return outcome switch
            {
                AssetFetchOutcome.Ok ok => CacheAndReturnAsset(assetRef, ok),
                AssetFetchOutcome.HashMismatch mismatch => WithheldAssetHashMismatch(slug, assetRef, mismatch),
                AssetFetchOutcome.Oversize => WithheldAssetOversize(slug, assetRef),
                AssetFetchOutcome.NetworkFailure => ServeStaleAssetOrUnreachable(assetRef.Path),
                // AssetFetchOutcome's constructor is private (closed hierarchy) — this arm can never
                // actually run; kept for the same Roslyn-exhaustiveness reason as GetEntryAsync's own
                // discard arm above.
                _ => throw new UnreachableException($"Unhandled {nameof(AssetFetchOutcome)} case."),
            };
        }
        finally
        {
            singleFlight.Release();
        }
    }

    // ── Cache reads (all under cacheGate; never span an await) ─────────────────────────────────

    bool TryServeFreshIndex(string url, [NotNullWhen(true)] out CatalogIndexFetchResult.Ok? result)
    {
        lock (cacheGate)
        {
            if (cachedIndex is { } snapshot && snapshot.SourceUrl == url
                && timeProvider.GetUtcNow() - snapshot.FetchedAt < CacheTtl)
            {
                result = new CatalogIndexFetchResult.Ok(snapshot.Entries, snapshot.FetchedAt);
                return true;
            }
        }

        result = null;
        return false;
    }

    bool TryServeFreshEntry(string slug, [NotNullWhen(true)] out CatalogEntryFetchResult.Ok? result)
    {
        lock (cacheGate)
        {
            if (cachedEntries.TryGetValue(slug, out var snapshot)
                && timeProvider.GetUtcNow() - snapshot.FetchedAt < CacheTtl)
            {
                result = new CatalogEntryFetchResult.Ok(snapshot.Content, snapshot.FetchedAt);
                return true;
            }
        }

        result = null;
        return false;
    }

    bool TryResolveSummary(string slug, [NotNullWhen(true)] out CatalogEntrySummary? summary, out Uri directory)
    {
        lock (cacheGate)
        {
            // GetIndexAsync just returned Ok immediately before this is called, so cachedIndex is
            // guaranteed populated — this read and every writer share the same cacheGate, so no
            // other caller can have cleared it in between.
            var snapshot = cachedIndex ?? throw new UnreachableException(
                "Catalog index cache was empty immediately after a successful GetIndexAsync call.");
            directory = snapshot.Directory;
            summary = snapshot.Entries.FirstOrDefault(e => e.Slug == slug);
            return summary is not null;
        }
    }

    bool TryServeFreshAsset(string assetPath, [NotNullWhen(true)] out CatalogAssetFetchResult.Ok? result)
    {
        lock (cacheGate)
        {
            if (cachedAssets.TryGetValue(assetPath, out var snapshot)
                && timeProvider.GetUtcNow() - snapshot.FetchedAt < CacheTtl)
            {
                result = new CatalogAssetFetchResult.Ok(snapshot.Bytes, snapshot.FetchedAt);
                return true;
            }
        }

        result = null;
        return false;
    }

    /// <summary>
    /// Resolves <paramref name="file"/> (a bare filename, e.g. <c>"space-grotesk-variable-latin.woff2"</c>)
    /// against the current index's entry for <paramref name="slug"/> — same "same cacheGate as every
    /// writer, so this can only run once <see cref="GetIndexAsync"/> guaranteed a populated cache"
    /// reasoning as <see cref="TryResolveSummary"/>.
    /// </summary>
    bool TryResolveAsset(string slug, string file, [NotNullWhen(true)] out CatalogAssetRef? assetRef, out Uri directory)
    {
        lock (cacheGate)
        {
            var snapshot = cachedIndex ?? throw new UnreachableException(
                "Catalog index cache was empty immediately after a successful GetIndexAsync call.");
            directory = snapshot.Directory;
            var summary = snapshot.Entries.FirstOrDefault(e => e.Slug == slug);
            assetRef = summary?.Assets.FirstOrDefault(a => Path.GetFileName(a.Path) == file);
            return assetRef is not null;
        }
    }

    // ── Entry outcome mapping (cache write / WARN log, then the public result) ─────────────────

    CatalogEntryFetchResult.Ok CacheAndReturnEntry(string slug, CatalogEntrySummary summary, EntryFetchOutcome.Ok ok)
    {
        var fetchedAt = timeProvider.GetUtcNow();
        var content = new CatalogEntryContent(summary.Slug, summary.Kind, summary.Audience, summary.BestFor, ok.ManifestJson, ok.MetaJson, summary.Assets);
        lock (cacheGate)
        {
            cachedEntries[slug] = new CachedEntry(content, summary.Manifest.Sha256, summary.Meta.Sha256, fetchedAt);
            EvictOldestIfOverCapacity();
        }

        return new CatalogEntryFetchResult.Ok(content, fetchedAt);
    }

    /// <summary>Called under <see cref="cacheGate"/>. See <see cref="MaxCachedEntries"/>'s own remarks.</summary>
    void EvictOldestIfOverCapacity()
    {
        while (cachedEntries.Count > MaxCachedEntries)
            cachedEntries.Remove(cachedEntries.MinBy(pair => pair.Value.FetchedAt).Key);
    }

    /// <summary>
    /// Called under <see cref="cacheGate"/>, right after <see cref="cachedIndex"/> is replaced with
    /// a freshly fetched index (review finding). A blanket <c>cachedEntries.Clear()</c> here would
    /// defeat entry-level stale-on-failure (F90.4) on a common partial-outage shape: the index
    /// origin recovers a moment before an entry's own origin does, and every already-cached entry
    /// would wrongly go cold even though NOTHING about them actually changed. Instead, a cached
    /// slug is dropped only when the new index either no longer lists it, or lists it with a
    /// DIFFERENT manifest/meta sha256 than the bytes currently cached under it (the F90.3 hash
    /// contract this cache promised its content matches) — every other cached slug keeps its
    /// content AND its original fetched-at, unaffected by this index refresh.
    ///
    /// <para>
    /// DUPLICATE-SLUG TOLERANT (F1 review finding, T194): <see cref="CatalogIndexValidator"/> has no
    /// cross-entry slug-uniqueness check — a hand-built or hostile index CAN validly declare two
    /// entries sharing the same <see cref="CatalogEntrySummary.Slug"/>. The lookup below is built by
    /// indexer assignment (last-one-wins), never <c>ToDictionary</c> (which throws
    /// <see cref="ArgumentException"/> on a duplicate key) — this method must never throw regardless
    /// of what shape <paramref name="currentEntries"/> turns out to have, since it runs on EVERY
    /// successful fetch (including the very first, cold-cache one), straight out from under
    /// <see cref="cacheGate"/>'s lock.
    /// </para>
    /// </summary>
    void PruneChangedEntries(IReadOnlyList<CatalogEntrySummary> currentEntries)
    {
        var bySlug = new Dictionary<string, CatalogEntrySummary>(StringComparer.Ordinal);
        foreach (var entry in currentEntries)
            bySlug[entry.Slug] = entry;

        foreach (var slug in cachedEntries.Keys.ToArray())
        {
            if (bySlug.TryGetValue(slug, out var current)
                && cachedEntries[slug].ManifestSha256 == current.Manifest.Sha256
                && cachedEntries[slug].MetaSha256 == current.Meta.Sha256)
                continue;

            cachedEntries.Remove(slug);
        }
    }

    CatalogEntryFetchResult.HashMismatch WithheldHashMismatch(string slug, EntryFetchOutcome.HashMismatch mismatch)
    {
        logger.LogWarning(
            "Persona catalog entry withheld: slug={Slug} part={Part} expected={Expected} actual={Actual}",
            LogSafeText.Sanitize(slug), mismatch.Part, LogSafeText.Sanitize(mismatch.Expected), LogSafeText.Sanitize(mismatch.Actual));
        return new CatalogEntryFetchResult.HashMismatch(slug, mismatch.Part, mismatch.Expected, mismatch.Actual);
    }

    CatalogEntryFetchResult.Oversize WithheldOversize(string slug, EntryFetchOutcome.Oversize oversize)
    {
        logger.LogWarning("Persona catalog entry withheld: slug={Slug} part={Part} exceeded its size cap", LogSafeText.Sanitize(slug), oversize.Part);
        return new CatalogEntryFetchResult.Oversize(slug, oversize.Part);
    }

    CatalogEntryFetchResult ServeStaleEntryOrUnreachable(string slug)
    {
        // Stale-on-failure (F90.4) at entry granularity: a previously good fetch for THIS slug
        // beats nothing, even past its own TTL.
        lock (cacheGate)
        {
            if (cachedEntries.TryGetValue(slug, out var stale))
                return new CatalogEntryFetchResult.Ok(stale.Content, stale.FetchedAt);
        }

        return new CatalogEntryFetchResult.Unreachable();
    }

    // ── Asset outcome mapping (cache write / WARN log, then the public result) ─────────────────

    CatalogAssetFetchResult.Ok CacheAndReturnAsset(CatalogAssetRef assetRef, AssetFetchOutcome.Ok ok)
    {
        var fetchedAt = timeProvider.GetUtcNow();
        lock (cacheGate)
        {
            cachedAssets[assetRef.Path] = new CachedAsset(ok.Bytes, assetRef.Sha256, fetchedAt);
            EvictOldestAssetIfOverCapacity();
        }

        return new CatalogAssetFetchResult.Ok(ok.Bytes, fetchedAt);
    }

    /// <summary>Called under <see cref="cacheGate"/>. See <see cref="MaxCachedAssets"/>'s own remarks.</summary>
    void EvictOldestAssetIfOverCapacity()
    {
        while (cachedAssets.Count > MaxCachedAssets)
            cachedAssets.Remove(cachedAssets.MinBy(pair => pair.Value.FetchedAt).Key);
    }

    /// <summary>
    /// Called under <see cref="cacheGate"/>, right after <see cref="cachedIndex"/> is replaced —
    /// the exact same reasoning as <see cref="PruneChangedEntries"/> (its own remarks), applied to
    /// <see cref="cachedAssets"/>: a cached asset is dropped only when the refreshed index either no
    /// longer declares that exact path, or declares it with a DIFFERENT sha256 than the bytes
    /// currently cached under it.
    ///
    /// <para>
    /// DUPLICATE-PATH TOLERANT (F1 review finding, T194): <see cref="CatalogIndexValidator.TryValidateAssets"/>
    /// now rejects a single entry that declares the same asset path twice, but two SEPARATE entries
    /// sharing the same slug (the sibling <see cref="PruneChangedEntries"/> hazard — nothing stops
    /// that at the index level either) can still produce two <see cref="CatalogAssetRef"/>s with the
    /// identical <see cref="CatalogAssetRef.Path"/>. The lookup below is built by indexer assignment
    /// (last-one-wins), never <c>ToDictionary</c> (which throws <see cref="ArgumentException"/> on a
    /// duplicate key) — this ran unconditionally on every fetch, including a totally cold cache, so a
    /// throw here was a genuine unhandled 500 on every catalog route, not merely a cache-staleness bug.
    /// </para>
    /// </summary>
    void PruneChangedAssets(IReadOnlyList<CatalogEntrySummary> currentEntries)
    {
        var byPath = new Dictionary<string, CatalogAssetRef>(StringComparer.Ordinal);
        foreach (var asset in currentEntries.SelectMany(e => e.Assets))
            byPath[asset.Path] = asset;

        foreach (var path in cachedAssets.Keys.ToArray())
        {
            if (byPath.TryGetValue(path, out var current) && cachedAssets[path].Sha256 == current.Sha256)
                continue;

            cachedAssets.Remove(path);
        }
    }

    CatalogAssetFetchResult.HashMismatch WithheldAssetHashMismatch(string slug, CatalogAssetRef assetRef, AssetFetchOutcome.HashMismatch mismatch)
    {
        var file = Path.GetFileName(assetRef.Path);
        logger.LogWarning(
            "Persona catalog asset withheld: slug={Slug} file={File} expected={Expected} actual={Actual}",
            LogSafeText.Sanitize(slug), LogSafeText.Sanitize(file), LogSafeText.Sanitize(mismatch.Expected), LogSafeText.Sanitize(mismatch.Actual));
        return new CatalogAssetFetchResult.HashMismatch(slug, file);
    }

    CatalogAssetFetchResult.Oversize WithheldAssetOversize(string slug, CatalogAssetRef assetRef)
    {
        var file = Path.GetFileName(assetRef.Path);
        logger.LogWarning("Persona catalog asset withheld: slug={Slug} file={File} exceeded its size cap", LogSafeText.Sanitize(slug), LogSafeText.Sanitize(file));
        return new CatalogAssetFetchResult.Oversize(slug, file);
    }

    CatalogAssetFetchResult ServeStaleAssetOrUnreachable(string assetPath)
    {
        // Stale-on-failure (F90.4) at asset granularity, same TTL-parity posture as entries.
        lock (cacheGate)
        {
            if (cachedAssets.TryGetValue(assetPath, out var stale))
                return new CatalogAssetFetchResult.Ok(stale.Bytes, stale.FetchedAt);
        }

        return new CatalogAssetFetchResult.Unreachable();
    }

    // ── HTTP fetch (CatalogHttpFetcher owns the request/bounded-read mechanics) ────────────────

    async Task<IReadOnlyList<CatalogEntrySummary>?> FetchAndValidateIndexAsync(Uri indexUri, Uri directory, CancellationToken ct)
    {
        var outcome = await CatalogHttpFetcher.FetchAsync(httpClientFactory, indexUri, MaxIndexBytes, ct);
        switch (outcome)
        {
            case CatalogFetchOutcome.Ok ok:
                if (CatalogIndexValidator.TryValidate(ok.Bytes, directory, out var entries, out var reason))
                    return entries;

                logger.LogWarning("Persona catalog index rejected: {Reason}", LogSafeText.Sanitize(reason));
                return null;

            case CatalogFetchOutcome.Oversize:
                logger.LogWarning("Persona catalog index rejected: exceeds the {MaxBytes}-byte size cap", MaxIndexBytes);
                return null;

            case CatalogFetchOutcome.NetworkFailure failure:
                logger.LogWarning("Persona catalog index fetch failed: {Detail}", LogSafeText.Sanitize(failure.Detail));
                return null;

            default:
                // CatalogFetchOutcome's constructor is private (closed hierarchy) — this arm can
                // never actually run; see GetEntryAsync's own discard arm for why a closed
                // hierarchy still needs one (Roslyn doesn't treat it as provably exhaustive).
                throw new UnreachableException($"Unhandled {nameof(CatalogFetchOutcome)} case.");
        }
    }

    async Task<EntryFetchOutcome> FetchAndVerifyEntryAsync(CatalogEntrySummary summary, Uri directory, CancellationToken ct)
    {
        var manifest = await FetchAndVerifyFileAsync(directory, summary.Manifest, CatalogEntryFilePart.Manifest, MaxCardBytes, ct);
        if (manifest is not EntryFetchOutcome.FileOk manifestOk)
            return manifest;

        var meta = await FetchAndVerifyFileAsync(directory, summary.Meta, CatalogEntryFilePart.Meta, MaxMetaBytes, ct);
        if (meta is not EntryFetchOutcome.FileOk metaOk)
            return meta;

        return new EntryFetchOutcome.Ok(manifestOk.Text, metaOk.Text);
    }

    /// <summary>
    /// Fetches and hash-verifies ONE file (manifest or meta), returning either
    /// <see cref="EntryFetchOutcome.FileOk"/> (consumed only by <see cref="FetchAndVerifyEntryAsync"/>,
    /// which pairs two of these into the public <see cref="EntryFetchOutcome.Ok"/>) or one of the
    /// three failure cases, already tagged with <paramref name="part"/> (review finding — folded
    /// what used to be a separate per-file result type into this one, since both hierarchies were
    /// identical apart from that tag).
    /// </summary>
    async Task<EntryFetchOutcome> FetchAndVerifyFileAsync(Uri directory, CatalogFileRef fileRef, CatalogEntryFilePart part, int maxBytes, CancellationToken ct)
    {
        // Belt-and-braces (SPEC F90.2): CatalogIndexValidator already proved this path resolves
        // under `directory` when the index itself was accepted — re-derive and re-check right here
        // too, the one place bytes actually leave this process for a remote fetch. A failure here
        // means the index cache itself is inconsistent (a real bug), not a network condition — it
        // throws rather than being swallowed into a soft "network failure" outcome.
        if (!CatalogIndexValidator.TryResolveWithinDirectory(directory, fileRef.Path, out var uri))
            throw new UnreachableException($"'{fileRef.Path}' no longer resolves under its index directory.");

        var outcome = await CatalogHttpFetcher.FetchAsync(httpClientFactory, uri, maxBytes, ct);
        return outcome switch
        {
            CatalogFetchOutcome.Ok ok => VerifyHash(ok.Bytes, fileRef, part),
            CatalogFetchOutcome.Oversize => new EntryFetchOutcome.Oversize(part),
            CatalogFetchOutcome.NetworkFailure => new EntryFetchOutcome.NetworkFailure(),
            _ => throw new UnreachableException($"Unhandled {nameof(CatalogFetchOutcome)} case."),
        };
    }

    static EntryFetchOutcome VerifyHash(byte[] bytes, CatalogFileRef fileRef, CatalogEntryFilePart part)
    {
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return string.Equals(actualHash, fileRef.Sha256, StringComparison.Ordinal)
            ? new EntryFetchOutcome.FileOk(part, Encoding.UTF8.GetString(bytes))
            : new EntryFetchOutcome.HashMismatch(part, fileRef.Sha256, actualHash);
    }

    /// <summary>
    /// Fetches and hash-verifies ONE binary asset (SPEC F104.1, T194) — the same belt-and-braces
    /// directory re-check <see cref="FetchAndVerifyFileAsync"/> already does for a manifest/meta
    /// pointer, applied to <see cref="CatalogAssetRef"/>. The size cap actually passed to the bounded
    /// read is <c>min(the asset's own declared <see cref="CatalogAssetRef.Bytes"/>, <see cref="MaxAssetBytes"/>)</c>
    /// (T193 review obligation) — <paramref name="assetRef"/>'s declared size is untrusted origin
    /// content and must never be the ONLY bound the stream read trusts. The cast to <see langword="int"/>
    /// is always safe here: <see cref="Math.Min(long, long)"/> can only ever return
    /// <see cref="MaxAssetBytes"/> itself (an <see langword="int"/> literal) when the declared size is
    /// the larger operand, and the declared size when IT is smaller — <see cref="CatalogIndexValidator"/>
    /// already proved that value positive before this ever ran.
    /// </summary>
    async Task<AssetFetchOutcome> FetchAndVerifyAssetAsync(Uri directory, CatalogAssetRef assetRef, CancellationToken ct)
    {
        if (!CatalogIndexValidator.TryResolveWithinDirectory(directory, assetRef.Path, out var uri))
            throw new UnreachableException($"'{assetRef.Path}' no longer resolves under its index directory.");

        var effectiveCap = (int)Math.Min(assetRef.Bytes, MaxAssetBytes);
        var outcome = await CatalogHttpFetcher.FetchAsync(httpClientFactory, uri, effectiveCap, ct);
        return outcome switch
        {
            CatalogFetchOutcome.Ok ok => VerifyAssetHash(ok.Bytes, assetRef),
            CatalogFetchOutcome.Oversize => new AssetFetchOutcome.Oversize(),
            CatalogFetchOutcome.NetworkFailure => new AssetFetchOutcome.NetworkFailure(),
            _ => throw new UnreachableException($"Unhandled {nameof(CatalogFetchOutcome)} case."),
        };
    }

    static AssetFetchOutcome VerifyAssetHash(byte[] bytes, CatalogAssetRef assetRef)
    {
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return string.Equals(actualHash, assetRef.Sha256, StringComparison.Ordinal)
            ? new AssetFetchOutcome.Ok(bytes)
            : new AssetFetchOutcome.HashMismatch(assetRef.Sha256, actualHash);
    }

    /// <summary>
    /// The index URL's own directory (SPEC F90.2 — entry paths resolve ONLY against this): RFC 3986
    /// base-URI resolution of "." against <paramref name="indexUri"/>, exactly what a browser or
    /// curl would treat as "the directory containing this file". Review finding: a prior version
    /// string-sliced <see cref="Uri.AbsoluteUri"/> at the last '/', which silently mistook a '/'
    /// inside a query string or fragment (both legal in an operator-configured URL — SettingValidator
    /// only checks "absolute http/https", not "has no query") for the path separator, computing a
    /// directory nothing resolves under and bricking the whole catalog with a WARN blaming an
    /// innocent entry path. <see cref="Uri"/>'s own relative-resolution constructor strips the
    /// query/fragment and normalizes dot segments per RFC 3986, so it is correct regardless.
    /// </summary>
    static Uri ResolveDirectory(Uri indexUri) => new(indexUri, ".");

    sealed record CachedIndex(string SourceUrl, Uri Directory, IReadOnlyList<CatalogEntrySummary> Entries, DateTimeOffset FetchedAt);

    /// <summary>
    /// <see cref="ManifestSha256"/>/<see cref="MetaSha256"/> are the hashes THIS content was
    /// verified against when fetched — carried alongside <see cref="Content"/> so
    /// <see cref="PruneChangedEntries"/> can tell "the index still says this slug's bytes are
    /// exactly these" from "the index moved on without this cache knowing" purely by comparison,
    /// with no re-fetch.
    /// </summary>
    sealed record CachedEntry(CatalogEntryContent Content, string ManifestSha256, string MetaSha256, DateTimeOffset FetchedAt);

    /// <summary>
    /// <see cref="Sha256"/> is the hash THIS content was verified against when fetched — mirrors
    /// <see cref="CachedEntry"/>'s own <c>ManifestSha256</c>/<c>MetaSha256</c> fields, letting
    /// <see cref="PruneChangedAssets"/> tell "the index still declares this exact asset" from "the
    /// index moved on" with no re-fetch.
    /// </summary>
    sealed record CachedAsset(byte[] Bytes, string Sha256, DateTimeOffset FetchedAt);
}
