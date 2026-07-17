using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// Hand-rolled fake of the Liquidsoap control plane for §0.1 acceptance gate tests.
/// Starts with a safe-rotation token on-air so the <see cref="GenWave.Core.Playout.PlayoutFeeder"/>
/// immediately sees a drained queue and pulls on the first tick. After a push, it reports the
/// last-pushed item as on-air so the feeder continuously advances and requests more items.
/// All pushes are recorded.
/// </summary>
sealed class FakeLiquidsoapControl : ILiquidsoapControl
{
    /// <summary>
    /// The initial on-air token — simulates the safe rotation running before any real track is pushed.
    /// A token with no corresponding media id tells the feeder the queue is drained.
    /// </summary>
    const string SafeRotationToken = "safe";

    MediaItem? lastPushed;

    public List<MediaItem> Pushed { get; } = [];
    public List<double> PushedGains { get; } = [];

    // Returns the safe-rotation token until the first push, then reports the last pushed item's id.
    public Task<string?> OnAirNewestAsync(CancellationToken ct)
    {
        string? id = lastPushed is not null ? lastPushed.MediaId : (string?)SafeRotationToken;
        return Task.FromResult(id);
    }

    public Task<EngineMetadata> MetadataAsync(string rid, CancellationToken ct)
    {
        // Only items that were pushed by the feeder carry a real media id.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (lastPushed is not null && lastPushed.MediaId == rid)
            map["track_id"] = rid;
        // SafeRotationToken has no track_id entry — feeder reads that as a drained queue.
        return Task.FromResult(new EngineMetadata(map));
    }

    public Task<string> PushAsync(MediaItem item, double gainDb, CancellationToken ct)
    {
        Pushed.Add(item);
        PushedGains.Add(gainDb);
        lastPushed = item;
        return Task.FromResult(item.MediaId);
    }
}
