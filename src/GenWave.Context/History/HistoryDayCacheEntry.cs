namespace GenWave.Context.History;

/// <summary>
/// One curated On-This-Day fact as persisted in a <see cref="HistoryDayCache"/> — a year and its
/// (unsanitized — see <see cref="ContextFactSanitizer"/>'s own remarks for why sanitizing happens at
/// the <see cref="ContextPipeline"/> chokepoint, never here or in <see cref="HistoryContextProvider"/>
/// itself) one-line description, straight from a <see cref="WikimediaSelectedEvent"/> whose
/// <see cref="WikimediaSelectedEvent.Year"/>/<see cref="WikimediaSelectedEvent.Text"/> were both
/// present (SPEC F109.1's "unknown/absent fields ... skip" applies per-entry here, before either ever
/// reaches this type).
/// </summary>
sealed record HistoryDayCacheEntry(int Year, string Text);
