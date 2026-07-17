using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Options;

namespace GenWave.Host.Engine;

/// <summary>
/// TCP client for the Liquidsoap telnet control protocol. One short-lived connection per command:
/// connect, send a single line, read until the END sentinel, close.
/// <para>
/// "What is on air now" is read from the <b>output's</b> metadata command (PRD §0), never from
/// request-level state. Liquidsoap 2.4 removed <c>request.on_air</c> because a request behind a
/// <c>switch</c>/<c>fallback</c> can report as playing when it is not — exactly GenWave's
/// <c>q → amplify → crossfade → fallback([main, safe])</c> topology. The earlier
/// <c>request.all</c> − <c>q.queue</c> workaround had latent drain mis-detection; the output's
/// metadata (carrying our exported <c>track_id</c> + <c>on_air</c>) is the reliable signal the engine
/// maintainers endorse. There is deliberately NO queue-listing here.
/// </para>
/// </summary>
sealed class LiquidsoapControl(
    LiquidsoapOptions options,
    string stationId,
    IStationIdentityProvider identityProvider,
    ILogger<LiquidsoapControl> log)
    : ILiquidsoapControl
{
    readonly LiquidsoapOptions cfg = options;

    /// <summary>The stamped id the feeder keys on — matches the annotate field in <see cref="PushAsync"/>
    /// and the export list in genwave.liq.</summary>
    const string TrackIdField = "track_id";

    /// <summary>Returned when the output is airing something that is NOT one of our pushed tracks
    /// (the safe rotation): a stable, non-null token distinct from any numeric track id, so the
    /// feeder sees a changed on-air id and treats it as a drained queue. Never collides with a real
    /// track id (those are numeric).</summary>
    internal const string DrainToken = "__drained__";

    /// <summary>
    /// The stamped id of the current on-air track, read from the output metadata: the numeric
    /// <c>track_id</c> when one of our tracks is airing, <see cref="DrainToken"/> when the safe
    /// rotation is airing (no stamped id ⇒ the queue drained), or null when nothing has aired yet.
    /// Advancement is a change in this id — never RID arithmetic (PRD §0 req 2/3/4).
    /// </summary>
    public async Task<string?> OnAirNewestAsync(CancellationToken ct)
    {
        var current = await ReadOutputMetadataAsync(ct);
        if (current.Count == 0) return null;                       // nothing resolved yet (cold start)
        return current.TryGetValue(TrackIdField, out var id) && id.Length > 0
            ? id                                                   // a real track is airing
            : DrainToken;                                          // safe rotation airing ⇒ drained
    }

    /// <summary>
    /// The current on-air metadata. The on-air read is the output's metadata snapshot, so the
    /// <paramref name="rid"/> identity token from <see cref="OnAirNewestAsync"/> is not re-resolved
    /// at the request level — the same live snapshot is returned (it carries our exported
    /// <c>track_id</c>/<c>pos</c>/<c>on_air</c>).
    /// </summary>
    public async Task<EngineMetadata> MetadataAsync(string rid, CancellationToken ct)
        => new(await ReadOutputMetadataAsync(ct));

    public async Task<string> PushAsync(MediaItem item, double gainDb, CancellationToken ct)
    {
        // Annotation construction is centralised in LiquidsoapAnnotationBuilder.Build so the
        // safe-track endpoint (K2b) produces byte-identical strings without duplicating logic.
        // Station name is read live (SPEC F44.1, gitea-#196) — never cached in a field — so a
        // Station:Name settings edit is stamped onto the very next push, no api restart.
        var annotation = LiquidsoapAnnotationBuilder.Build(
            item, gainDb, stationId, identityProvider.Current.Name);
        var response = await SendAsync($"{cfg.QueueId}.push {annotation}", ct);
        // A successful q.push returns a numeric RID; anything else is a rejection (e.g. "ERROR").
        // Validate and throw so the caller's try/catch logs a visible error and Fix 1's drain-retry
        // recovers on the next tick rather than silently leaving the feeder believing prepared==1.
        if (!long.TryParse(response.Trim(), out _))
            throw new InvalidOperationException(
                $"Engine rejected push (expected numeric RID): {response}");
        return response.Trim();
    }

    async Task<IReadOnlyDictionary<string, string>> ReadOutputMetadataAsync(CancellationToken ct)
        => ParseCurrentFrame(await SendAsync(cfg.OutputMetadataCommand, ct));

    /// <summary>
    /// Parse the output metadata reply, which is a history of recent tracks as numbered frames
    /// (<c>--- N ---</c>) of <c>key="value"</c> lines. Frame <c>--- 1 ---</c> is the current/newest
    /// track, so only its fields are returned. Falls back to parsing all lines if no frame markers
    /// are present (older/edge output formats).
    /// </summary>
    static IReadOnlyDictionary<string, string> ParseCurrentFrame(string reply)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sawFrame = false;
        var inCurrent = false;

        foreach (var raw in reply.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (IsFrameMarker(line, out var frame))
            {
                sawFrame = true;
                inCurrent = frame == 1;     // frame 1 == current/newest on-air track
                if (inCurrent) map.Clear();  // defensive: keep only the current frame's fields
                continue;
            }

            if (sawFrame && !inCurrent) continue;   // skip older frames once we know they exist

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            map[line[..eq].Trim()] = line[(eq + 1)..].Trim().Trim('"');
        }

        return map;
    }

    /// <summary>Matches a frame marker line like <c>--- 1 ---</c> and extracts its number.</summary>
    static bool IsFrameMarker(string line, out int frame)
    {
        frame = 0;
        if (!line.StartsWith("---", StringComparison.Ordinal)) return false;
        var inner = line.Trim('-', ' ');
        return int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out frame);
    }

    // --- transport: send one command, collect the reply up to the END terminator line ---
    async Task<string> SendAsync(string command, CancellationToken ct)
    {
        log.LogDebug("Liquidsoap command: {Command}", command);

        using var client = new TcpClient();
        await client.ConnectAsync(cfg.Host, cfg.Port, ct);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\n"), ct);

        var sb = new StringBuilder();
        var buf = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
        {
            sb.Append(Encoding.UTF8.GetString(buf, 0, read));
            // Each Liquidsoap response is terminated by "END" on its own line.
            if (sb.ToString().Replace("\r", "").Split('\n').Contains("END")) break;
        }

        var lines = sb.ToString().Replace("\r", "").Split('\n');
        return string.Join('\n', lines.TakeWhile(l => l != "END")).Trim();
    }
}
