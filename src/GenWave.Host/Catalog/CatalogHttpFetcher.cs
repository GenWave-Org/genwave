namespace GenWave.Host.Catalog;

using System.Reflection;

/// <summary>
/// Shared HTTP fetch + bounded-read mechanics for <see cref="CatalogProxyService"/>'s two upstream
/// calls (the index, and one card/meta file) — the part that's identical either way: build the
/// request (with the MusicBrainz-etiquette User-Agent, SPEC F76.1's shape), send it via the
/// <see cref="CatalogProxyService.HttpClientName"/> client, and read the body up to a cap WHILE
/// reading — never buffering unbounded first (SPEC F90.3). Kept separate from
/// <see cref="CatalogProxyService"/> (single responsibility): this class only ever answers "did the
/// bytes arrive, and were they small enough" — never "are they the RIGHT bytes" (hash verification)
/// or "is this a trustworthy shelf" (index validation), both of which need context this class
/// deliberately doesn't carry.
/// </summary>
internal static class CatalogHttpFetcher
{
    /// <summary>
    /// "GenWave/&lt;version&gt; (+repo)" — the same shape as <c>MusicBrainzYearLookup.UserAgent</c>
    /// (SPEC F76.1). Read once from this assembly's own build-stamped
    /// <see cref="AssemblyInformationalVersionAttribute"/> (SPEC F65.1), never a hardcoded literal.
    /// </summary>
    static readonly string UserAgent =
        $"GenWave/{typeof(CatalogHttpFetcher).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"} (+https://github.com/GenWave-Org/genwave)";

    public static async Task<CatalogFetchOutcome> FetchAsync(
        IHttpClientFactory httpClientFactory, Uri uri, int maxBytes, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            // HttpCompletionOption.ResponseHeadersRead (review finding) — the default,
            // ResponseContentRead, makes SendAsync itself buffer the ENTIRE body up front before
            // this method ever sees a byte, making ReadBoundedAsync's own cap dead code: every
            // oversize response failed inside SendAsync as an ordinary exception (NetworkFailure),
            // never Oversize. With ResponseHeadersRead, only the headers are read here;
            // ReadBoundedAsync below is the ONLY place the body is actually read, so it is
            // genuinely what enforces the cap — HttpClient.MaxResponseContentBufferSize (set in
            // Program.cs) plays NO part in that anymore; see this method's own `using` below for
            // why NOT disposing the response promptly is the other half of this same review finding.
            using var response = await httpClientFactory.CreateClient(CatalogProxyService.HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            // Throws on non-2xx — INCLUDING a 3xx redirect (this client's primary handler disables
            // AllowAutoRedirect, so a redirect target is never followed; the 3xx itself lands here
            // as a plain fetch failure, per the SSRF ruling in CatalogProxyService's own remarks).
            // Review finding: under ResponseHeadersRead the body is STILL on the wire when this
            // throws — without the `using` above, every failed fetch (a non-2xx status, which is
            // this catalog's DEFAULT state pre-launch, since the origin repo isn't public yet) would
            // leak the pooled connection instead of returning it, exhausting the pool under any
            // sustained outage.
            response.EnsureSuccessStatusCode();

            var (bytes, oversized) = await ReadBoundedAsync(response.Content, maxBytes, ct);
            return oversized ? new CatalogFetchOutcome.Oversize() : new CatalogFetchOutcome.Ok(bytes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller cancelled — not an upstream failure to report.
            throw;
        }
        catch (Exception ex)
        {
            return new CatalogFetchOutcome.NetworkFailure(ex.Message);
        }
    }

    /// <summary>
    /// Reads <paramref name="content"/> up to <paramref name="maxBytes"/>, never buffering past
    /// that point (SPEC F90.3) — mirrors <c>PersonaController.ReadBoundedBodyAsync</c>'s own shape.
    /// The declared <c>Content-Length</c> is a fast reject when present and honest; the running
    /// total while reading is what actually enforces the cap either way.
    /// </summary>
    static async Task<(byte[] Bytes, bool Oversized)> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength is long declared && declared > maxBytes)
            return ([], true);

        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return ([], true);

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), false);
    }
}
