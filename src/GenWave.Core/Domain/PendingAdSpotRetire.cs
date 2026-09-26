namespace GenWave.Core.Domain;

/// <summary>
/// One <c>station.ad_spot</c> row still waiting on its own eventual old-media turn-off (gh-#854, db/48's
/// <c>pending_retire_media_id</c>) — <c>Abstractions.IAdSpotStore.ListPendingRetiresAsync</c>'s own
/// element type. <see cref="SpotId"/> is the row a swap already committed;
/// <see cref="OldMediaId"/> is the media that swap displaced — not whatever <see cref="SpotId"/> now
/// points at (that already lives on <see cref="AdSpot.MediaId"/> by the time this row is ever read
/// back). The row stays in this list until <see cref="OldMediaId"/> is actually turned off ineligible
/// (or is found currently referenced by some OTHER spot's own <see cref="AdSpot.MediaId"/>, in which
/// case the marker simply clears without a flip).
/// </summary>
/// <param name="SpotId">The spot whose own guarded swap stamped this pending retire.</param>
/// <param name="OldMediaId">The old media id the swap displaced — the one still waiting to be turned
/// off ineligible.</param>
public sealed record PendingAdSpotRetire(long SpotId, long OldMediaId);
