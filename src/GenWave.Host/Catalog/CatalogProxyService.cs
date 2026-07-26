namespace GenWave.Host.Catalog;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using GenWave.Host.Options;

/// <summary>
/// One guarded door to the Persona Catalog shelf (STORY-234; SPEC F90.2-F90.4): fetches,
/// hash-verifies, and caches index.json plus individual entry card/meta documents from the single
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
/// card/meta sha256 is unchanged keeps its own cached content and stale-on-failure eligibility) —
/// see <see cref="PruneChangedEntries"/>.
/// </para>
///
/// <para>
/// SINGLE GLOBAL GATE, DELIBERATELY (doc fix, review finding): <see cref="singleFlight"/> is ONE
/// <see cref="SemaphoreSlim"/> for the WHOLE catalog surface, not one per resource — a concurrent
/// index refresh and an unrelated entry's card/meta fetch queue behind EACH OTHER, not just behind
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

    /// <summary>Size cap while reading a <c>&lt;slug&gt;.persona.json</c> card (SPEC F90.3, matches F89.2's own build-time cap).</summary>
    public const int MaxCardBytes = 256 * 1024;

    /// <summary>Size cap while reading a <c>&lt;slug&gt;.meta.json</c> document (SPEC F90.3, matches F89.2's own build-time cap).</summary>
    public const int MaxMetaBytes = 64 * 1024;

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
            logger.LogWarning("Persona catalog index rejected: '{Url}' is not an absolute http/https URL", url);
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

    /// <summary>GET one entry's hash-verified card + meta content (SPEC F90.2, F90.3). Resolves the index first — see <see cref="GetIndexAsync"/>.</summary>
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

    // ── Entry outcome mapping (cache write / WARN log, then the public result) ─────────────────

    CatalogEntryFetchResult.Ok CacheAndReturnEntry(string slug, CatalogEntrySummary summary, EntryFetchOutcome.Ok ok)
    {
        var fetchedAt = timeProvider.GetUtcNow();
        var content = new CatalogEntryContent(summary.Slug, summary.Audience, summary.BestFor, ok.CardJson, ok.MetaJson);
        lock (cacheGate)
        {
            cachedEntries[slug] = new CachedEntry(content, summary.Card.Sha256, summary.Meta.Sha256, fetchedAt);
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
    /// DIFFERENT card/meta sha256 than the bytes currently cached under it (the F90.3 hash contract
    /// this cache promised its content matches) — every other cached slug keeps its content AND its
    /// original fetched-at, unaffected by this index refresh.
    /// </summary>
    void PruneChangedEntries(IReadOnlyList<CatalogEntrySummary> currentEntries)
    {
        var bySlug = currentEntries.ToDictionary(e => e.Slug, StringComparer.Ordinal);
        foreach (var slug in cachedEntries.Keys.ToArray())
        {
            if (bySlug.TryGetValue(slug, out var current)
                && cachedEntries[slug].CardSha256 == current.Card.Sha256
                && cachedEntries[slug].MetaSha256 == current.Meta.Sha256)
                continue;

            cachedEntries.Remove(slug);
        }
    }

    CatalogEntryFetchResult.HashMismatch WithheldHashMismatch(string slug, EntryFetchOutcome.HashMismatch mismatch)
    {
        logger.LogWarning(
            "Persona catalog entry withheld: slug={Slug} part={Part} expected={Expected} actual={Actual}",
            slug, mismatch.Part, mismatch.Expected, mismatch.Actual);
        return new CatalogEntryFetchResult.HashMismatch(slug, mismatch.Part, mismatch.Expected, mismatch.Actual);
    }

    CatalogEntryFetchResult.Oversize WithheldOversize(string slug, EntryFetchOutcome.Oversize oversize)
    {
        logger.LogWarning("Persona catalog entry withheld: slug={Slug} part={Part} exceeded its size cap", slug, oversize.Part);
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

    // ── HTTP fetch (CatalogHttpFetcher owns the request/bounded-read mechanics) ────────────────

    async Task<IReadOnlyList<CatalogEntrySummary>?> FetchAndValidateIndexAsync(Uri indexUri, Uri directory, CancellationToken ct)
    {
        var outcome = await CatalogHttpFetcher.FetchAsync(httpClientFactory, indexUri, MaxIndexBytes, ct);
        switch (outcome)
        {
            case CatalogFetchOutcome.Ok ok:
                if (CatalogIndexValidator.TryValidate(ok.Bytes, directory, out var entries, out var reason))
                    return entries;

                logger.LogWarning("Persona catalog index rejected: {Reason}", reason);
                return null;

            case CatalogFetchOutcome.Oversize:
                logger.LogWarning("Persona catalog index rejected: exceeds the {MaxBytes}-byte size cap", MaxIndexBytes);
                return null;

            case CatalogFetchOutcome.NetworkFailure failure:
                logger.LogWarning("Persona catalog index fetch failed: {Detail}", failure.Detail);
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
        var card = await FetchAndVerifyFileAsync(directory, summary.Card, CatalogEntryFilePart.Card, MaxCardBytes, ct);
        if (card is not EntryFetchOutcome.FileOk cardOk)
            return card;

        var meta = await FetchAndVerifyFileAsync(directory, summary.Meta, CatalogEntryFilePart.Meta, MaxMetaBytes, ct);
        if (meta is not EntryFetchOutcome.FileOk metaOk)
            return meta;

        return new EntryFetchOutcome.Ok(cardOk.Text, metaOk.Text);
    }

    /// <summary>
    /// Fetches and hash-verifies ONE file (card or meta), returning either
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
    /// <see cref="CardSha256"/>/<see cref="MetaSha256"/> are the hashes THIS content was verified
    /// against when fetched — carried alongside <see cref="Content"/> so <see cref="PruneChangedEntries"/>
    /// can tell "the index still says this slug's bytes are exactly these" from "the index moved on
    /// without this cache knowing" purely by comparison, with no re-fetch.
    /// </summary>
    sealed record CachedEntry(CatalogEntryContent Content, string CardSha256, string MetaSha256, DateTimeOffset FetchedAt);
}
