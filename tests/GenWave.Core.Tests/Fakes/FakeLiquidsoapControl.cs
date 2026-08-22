using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Fakes;

/// <summary>
/// Hand-rolled fake of the engine control plane (no Moq — see DEVELOPMENT_BELIEFS). The on-air read is
/// now the OUTPUT metadata: each tick the test scripts the on-air id the engine reports — the track's
/// stamped media id for a real track, or any other token (e.g. "safe") for the drained safe rotation.
/// A token listed in <c>realIds</c> carries a media id (so <see cref="EngineMetadata.TryGetMediaId"/>
/// succeeds); any other token has none, which the feeder reads as a drained queue. Pushes are recorded.
/// </summary>
sealed class FakeLiquidsoapControl : ILiquidsoapControl
{
    readonly Queue<string?> onAir;
    readonly IReadOnlySet<string> realIds;
    readonly IReadOnlyDictionary<string, string> urlById;
    readonly string? pushArtworkUrl;
    string? lastOnAir;

    public List<MediaItem> Pushed { get; } = [];
    public List<double> PushedGains { get; } = [];

    /// <param name="onAirSequence">One scripted on-air id per tick.</param>
    /// <param name="realIds">Ids that carry a stamped media id (vs. a drain token).</param>
    /// <param name="urlById">
    /// Optional per-id <c>url</c> annotation field to surface on <see cref="MetadataAsync"/> (PLAN
    /// T125 review F2/F4) — stands in for genwave.liq's own echoed output metadata on an
    /// engine-initiated play, legitimate or hostile depending on what the test scripts here.
    /// </param>
    /// <param name="pushArtworkUrl">
    /// Optional <see cref="EnginePushResult.ArtworkUrl"/> every <see cref="PushAsync"/> call returns
    /// (PLAN T125 review F4) — stands in for the token url a real <c>ArtworkUrlResolver</c> would
    /// have resolved for the pushed item.
    /// </param>
    public FakeLiquidsoapControl(
        IEnumerable<string?> onAirSequence, IReadOnlySet<string> realIds,
        IReadOnlyDictionary<string, string>? urlById = null, string? pushArtworkUrl = null)
    {
        onAir = new Queue<string?>(onAirSequence);
        this.realIds = realIds;
        this.urlById = urlById ?? new Dictionary<string, string>(StringComparer.Ordinal);
        this.pushArtworkUrl = pushArtworkUrl;
    }

    // One scripted on-air id per tick; once the script is exhausted, hold the last value (steady state).
    public Task<string?> OnAirNewestAsync(CancellationToken ct)
    {
        if (onAir.Count > 0) lastOnAir = onAir.Dequeue();
        return Task.FromResult(lastOnAir);
    }

    public Task<EngineMetadata> MetadataAsync(string rid, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (realIds.Contains(rid)) map["track_id"] = rid;   // a real track carries its stamped media id
        if (urlById.TryGetValue(rid, out var url)) map["url"] = url;
        return Task.FromResult(new EngineMetadata(map));
    }

    /// <summary>Ids whose pushes are DECLINED (null result, nothing recorded) — stands in for a
    /// Host-side guard refusing the push (gh-#612), e.g. MediaExistencePushGuard on a missing file.</summary>
    public HashSet<string> DeclinePushIds { get; } = new(StringComparer.Ordinal);

    public Task<EnginePushResult?> PushAsync(MediaItem item, double gainDb, CancellationToken ct)
    {
        if (DeclinePushIds.Contains(item.MediaId)) return Task.FromResult<EnginePushResult?>(null);
        Pushed.Add(item);
        PushedGains.Add(gainDb);
        return Task.FromResult<EnginePushResult?>(new EnginePushResult("rid", pushArtworkUrl));
    }
}
