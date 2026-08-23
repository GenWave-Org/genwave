namespace GenWave.Host.Announcements;

/// <summary>
/// The aired-confirmation queue's own payload (SPEC F143.3, PLAN T343) — the unwrapped announcement
/// row id <see cref="AnnouncementAiredEventSink.Publish"/> already extracted via
/// <see cref="GenWave.Core.Domain.AnnouncementMediaId.TryUnwrap"/>, carried off the feeder's hot
/// <c>TrackAired</c> publish to <see cref="AnnouncementAiredDrainService"/>.
///
/// A dedicated record rather than a bare <see langword="long"/> — <c>RequestParsingServiceCollectionExtensions</c>'
/// own remarks name the exact hazard a second bare <c>Channel&lt;long&gt;</c>/<c>ChannelReader&lt;long&gt;</c>/
/// <c>ChannelWriter&lt;long&gt;</c> registration would open in this same container (the enrich-delta
/// queue and the wish-parse queue already claim those two shapes between them — see that class's own
/// remarks): <c>IServiceCollection.GetRequiredService&lt;T&gt;()</c> resolves the LAST registration for
/// a given closed type, so a THIRD bare-<see langword="long"/> channel would silently hand one queue's
/// reader/writer to a wholly unrelated consumer. This queue gets its own unambiguous element type
/// instead, closing that hazard by construction rather than by convention.
/// </summary>
sealed record AnnouncementAiredSignal(long AnnouncementId);
