namespace GenWave.Host.Playout;

/// <summary>
/// The rotation ledger's own queue payload (SPEC F149.2, STORY-367, PLAN T355) — mirrors
/// <c>GenWave.Host.Announcements.AnnouncementAiredSignal</c>'s own remarks verbatim for why this is
/// a dedicated record rather than a bare <see langword="long"/>/<see cref="DateTimeOffset"/> tuple:
/// <c>IServiceCollection.GetRequiredService{T}()</c> resolves the LAST registration for a given
/// closed generic <c>Channel{T}</c> type, so a second, differently-shaped bounded channel in this
/// same container would silently hand one queue's reader/writer to an unrelated consumer. This
/// queue gets its own unambiguous element type instead, closing that hazard by construction.
///
/// <paramref name="AiredAt"/> rides along rather than being re-read at drain time — the SAME
/// capture-at-publish-time discipline <c>GenWave.MediaLibrary.Station.BoothLogWriter</c>'s own
/// remarks establish for every other stamp on this hot path: a backlog under a DB outage must never
/// let a later real time creep into an earlier airing's own <c>last_aired_at</c>.
/// </summary>
sealed record MediaRotationAiredSignal(long MediaId, DateTimeOffset AiredAt);
