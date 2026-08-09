namespace GenWave.Context.History;

/// <summary>
/// The on-disk shape of one <c>{CacheRoot}/context/history/{MM-dd}.json</c> day file (SPEC F109.2) —
/// GenWave's OWN persisted schema, not <see cref="WikimediaOnThisDayResponse"/> serialized verbatim:
/// keeping the two separate means a future Wikimedia response-shape change can never silently corrupt
/// (or require a migration of) an already-cached day file, and the cache never carries fields this
/// provider has no use for in the first place (see <see cref="WikimediaSelectedEvent"/>'s own
/// remarks). This is the "trimmed payload" half of F109.2's "the raw (or trimmed) payload persisted
/// after fetch" — trimmed won, for exactly that decoupling reason.
/// </summary>
sealed record HistoryDayCache(IReadOnlyList<HistoryDayCacheEntry> Entries);
