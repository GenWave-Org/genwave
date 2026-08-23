namespace GenWave.Host.Announcements;

/// <summary>
/// The privacy-flip queue's own payload (SPEC F145.2, PLAN T343) — a pure marker: the decline sweep
/// it triggers (<see cref="AnnouncementPrivacyFlipDrainService"/>) carries no per-signal data of its
/// own (unlike <see cref="AnnouncementAiredSignal"/>, there is no id to carry — the sweep declines
/// EVERY currently live row, not one named row). A dedicated type rather than a bare
/// <see cref="bool"/>/<see cref="System.DateTimeOffset"/> for the same reason
/// <see cref="AnnouncementAiredSignal"/>'s own remarks give: a shared BCL element type would risk a
/// future second channel of the same shape silently colliding with this one's reader/writer
/// registration.
/// </summary>
sealed record AnnouncementPrivacyFlipSignal;
