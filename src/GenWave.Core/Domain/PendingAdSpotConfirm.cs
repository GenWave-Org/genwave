namespace GenWave.Core.Domain;

/// <summary>
/// One <c>station.ad_spot</c> row still waiting on its own eventual new-media eligibility confirm
/// (gh-#854, db/48's <c>pending_confirm_media_id</c>) — <c>Abstractions.IAdSpotStore.ListPendingConfirmsAsync</c>'s
/// own element type. Mirrors <see cref="PendingAdSpotRetire"/>'s own shape exactly, one marker over:
/// <see cref="SpotId"/> is the row a swap already committed; <see cref="NewMediaId"/> is the media
/// that swap landed — the SAME value <see cref="AdSpot.MediaId"/> already carries by the time this
/// row is ever read back. The row stays in this list until <see cref="NewMediaId"/> is actually
/// confirmed eligible, and the flip is only ever attempted while <see cref="SpotId"/> still names
/// <see cref="NewMediaId"/> as its own current <see cref="AdSpot.MediaId"/> AND is still
/// <see cref="AdState.Ready"/> — an operator retire between the swap's own commit and the flip must
/// never revive a row the operator meant to pull.
/// </summary>
/// <param name="SpotId">The spot whose own guarded swap stamped this pending confirm.</param>
/// <param name="NewMediaId">The new media id the swap landed — the one still waiting to be confirmed
/// eligible.</param>
public sealed record PendingAdSpotConfirm(long SpotId, long NewMediaId);
