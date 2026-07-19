namespace GenWave.Host.Stats;

using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Host.Options;

/// <summary>
/// Polls Icecast's password-protected admin stats (<c>GET {Icecast:StatsUrl}/admin/stats.xml</c>,
/// HTTP Basic auth <c>admin</c>/<c>{Icecast:AdminPassword}</c>, per icecast.xml.tmpl's
/// <c>&lt;admin-user&gt;</c>) for the <c>/stream</c> mount's live listener count (SPEC F62.12
/// addendum, STORY-179, gitea-#10). ANY failure — an unconfigured <see cref="IcecastOptions.StatsUrl"/>,
/// a non-2xx response, the ~2s request timeout (configured on this type's typed <c>HttpClient</c>
/// in Program.cs), or a stats.xml this cannot parse — degrades to null, logged at
/// <see cref="LogLevel.Debug"/>; this source never throws toward <see cref="Api.SpectatorController"/>
/// (F62.5's "never an error, never fabricated" discipline extended to listener count).
/// <para>
/// A ~10s internal memo guards the window beyond <see cref="Api.SpectatorOutputCachePolicies.NowPlaying"/>'s
/// own 5s dedupe: a cache MISS still shares one Icecast round-trip with any other miss inside the
/// memo window, so a spike of concurrent spectators never fans out into a spike of admin-stats
/// polls. A plain lock-guarded timestamped field — the <c>CachedVoiceLister</c> idiom (no
/// <c>IMemoryCache</c>: one singleton process holding exactly one cached value).
/// </para>
/// </summary>
public sealed class IcecastListenerStatsSource(
    HttpClient http,
    IOptionsMonitor<IcecastOptions> optionsMonitor,
    ILogger<IcecastListenerStatsSource> logger) : IListenerStatsSource
{
    static readonly TimeSpan MemoWindow = TimeSpan.FromSeconds(10);

    readonly object gate = new();
    int? memoValue;
    DateTimeOffset memoExpiresAt = DateTimeOffset.MinValue;

    public async Task<int?> GetListenerCountAsync(CancellationToken ct)
    {
        if (TryGetMemoized(out var memoized))
            return memoized;

        var value = await FetchAsync(ct);

        lock (gate)
        {
            memoValue = value;
            memoExpiresAt = DateTimeOffset.UtcNow + MemoWindow;
        }

        return value;
    }

    bool TryGetMemoized(out int? value)
    {
        lock (gate)
        {
            if (DateTimeOffset.UtcNow < memoExpiresAt)
            {
                value = memoValue;
                return true;
            }
        }

        value = null;
        return false;
    }

    async Task<int?> FetchAsync(CancellationToken ct)
    {
        var cfg = optionsMonitor.CurrentValue;
        if (string.IsNullOrEmpty(cfg.StatsUrl))
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildStatsUri(cfg.StatsUrl));
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"admin:{cfg.AdminPassword}")));

            var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

            var xml = await response.Content.ReadAsStringAsync(ct);
            return ParseStreamListenerCount(xml);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller cancelled (e.g. the spectator request was aborted) — not our own 2s
            // HttpClient timeout firing. Propagate; this is not a stats-poll failure to degrade.
            throw;
        }
        catch (Exception ex)
        {
            // Everything else lands here: our own 2s timeout, a non-2xx status
            // (EnsureSuccessStatusCode), a connect failure, or a stats.xml this cannot parse.
            // Every one of these degrades to an unknown (null) count — never thrown further.
            logger.LogDebug(ex, "Icecast listener-stats poll failed; reporting an unknown listener count");
            return null;
        }
    }

    static Uri BuildStatsUri(string statsUrl) => new($"{statsUrl.TrimEnd('/')}/admin/stats.xml");

    /// <summary>
    /// Sums the <c>&lt;listeners&gt;</c> value of every <c>&lt;source mount="/stream"&gt;</c>
    /// element (more than one is unusual but not impossible — a relay setup, say) — never any
    /// other mount, even if icecast.xml.tmpl someday grows a second one. Returns null, not zero,
    /// when no such element parses cleanly: this method cannot tell "genuinely nobody listening"
    /// apart from "stats.xml shape changed under us", and the honest answer to the latter is
    /// unknown, never a fabricated count.
    /// </summary>
    static int? ParseStreamListenerCount(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        if (root is null) return null;

        int? total = null;
        foreach (var source in root.Elements("source"))
        {
            if ((string?)source.Attribute("mount") != "/stream") continue;
            if (!int.TryParse(source.Element("listeners")?.Value, out var listeners)) continue;

            total = (total ?? 0) + listeners;
        }

        return total;
    }
}
