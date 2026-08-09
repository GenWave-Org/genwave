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
public sealed record StationImagingSettings(bool ClockAnchoredIdents, bool TimeAnnouncements);
