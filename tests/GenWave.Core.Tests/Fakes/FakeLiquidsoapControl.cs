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
    string? lastOnAir;

    public List<MediaItem> Pushed { get; } = [];
    public List<double> PushedGains { get; } = [];

    public FakeLiquidsoapControl(IEnumerable<string?> onAirSequence, IReadOnlySet<string> realIds)
    {
        onAir = new Queue<string?>(onAirSequence);
        this.realIds = realIds;
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
        return Task.FromResult(new EngineMetadata(map));
    }

    public Task<string> PushAsync(MediaItem item, double gainDb, CancellationToken ct)
    {
        Pushed.Add(item);
        PushedGains.Add(gainDb);
        return Task.FromResult("rid");
    }
}
