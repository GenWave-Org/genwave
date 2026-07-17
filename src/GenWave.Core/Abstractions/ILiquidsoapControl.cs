using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The C# side of the Liquidsoap telnet control protocol. "What is on air now" is read from the
/// OUTPUT's metadata (PRD §0) — the single source the engine maintainers endorse — never from
/// request-level state and never from the unreliable queue listing.
/// </summary>
public interface ILiquidsoapControl
{
    /// <summary>
    /// The stamped media id of the current on-air track, read from the output metadata; a stable
    /// non-null token when the safe rotation is airing (no stamped id ⇒ the queue drained); or null
    /// when nothing has aired yet. Advancement is detected by this value CHANGING — never by RID
    /// arithmetic (PRD §0).
    /// </summary>
    Task<string?> OnAirNewestAsync(CancellationToken ct);

    /// <summary>The current on-air metadata (from the output). Carries our stamped <c>track_id</c> when
    /// one of our tracks is airing; empty of it when the safe rotation is.</summary>
    Task<EngineMetadata> MetadataAsync(string rid, CancellationToken ct);

    /// <summary>
    /// Push a prepared track onto the engine queue with its per-track gain. The annotation stamps our
    /// media id (as <c>track_id</c>), which genwave.liq exports onto the output metadata so the on-air
    /// read can see it.
    /// </summary>
    Task<string> PushAsync(MediaItem item, double gainDb, CancellationToken ct);
}
