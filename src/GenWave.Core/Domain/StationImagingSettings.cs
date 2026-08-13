namespace GenWave.Core.Domain;

/// <summary>
/// The station's clock-anchored imaging knobs (SPEC F110.1/F110.3), read through
/// <see cref="Abstractions.IStationImagingSettingsProvider"/>. Bound to <c>Station:Imaging:*</c> —
/// both default <see langword="false"/>, so a fresh station keeps the existing
/// <c>StationIdEveryNUnits</c> unit-count cadence as its only ident source until an operator opts
/// one or both of these in (PLAN T230 acceptance: byte-identical sound while both stay false).
/// </summary>
/// <param name="ClockAnchoredIdents">
/// <c>Station:Imaging:ClockAnchoredIdents</c> (SPEC F110.1) — each top of hour enqueues a
/// future-dated <c>StationId</c> deferral, due at the station-local top-of-hour.
/// </param>
/// <param name="TimeAnnouncements">
/// <c>Station:Imaging:TimeAnnouncements</c> (SPEC F110.3) — each top of hour also enqueues a
/// future-dated <c>TimeDate</c> deferral, due at the SAME instant as <see cref="ClockAnchoredIdents"/>'s
/// own trigger.
/// </param>
/// <param name="TimeAnnouncementStaleMinutes">
/// <c>Station:Imaging:TimeAnnouncementStaleMinutes</c> (SPEC F124.4, PLAN T269) — the elapsed-due
/// expiry budget: a <c>TimeDate</c> deferral draining more than this many minutes past its own
/// air-time is dropped undrained rather than airing an hour that has already passed (SpeechDeferralQueue's
/// own <c>TryDequeueDue</c> remarks carry the exact air-time-lateness formula). Live-editable;
/// defaults to 5 — the shipped SPEC F124.4 budget, held here as a plain compile-time constant (unlike
/// <see cref="ClockAnchoredIdents"/>/<see cref="TimeAnnouncements"/>, this one has an honest non-off
/// default: idents are exempt by design, so this budget matters only once an operator opts
/// <see cref="TimeAnnouncements"/> in — see that param's own remarks).
/// </param>
public sealed record StationImagingSettings(
    bool ClockAnchoredIdents, bool TimeAnnouncements, int TimeAnnouncementStaleMinutes = 5);
