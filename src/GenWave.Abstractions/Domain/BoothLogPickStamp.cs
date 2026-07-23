namespace GenWave.Core.Domain;

/// <summary>
/// The exact shape persisted as <c>station.booth_log.pick</c> (SPEC F86.1, STORY-217, PLAN T73):
/// fired-rule summaries and the exploration flag, and nothing else. <see cref="PersonaPickDiagnostics.PoolSize"/>
/// and <see cref="PersonaPickDiagnostics.TopScores"/> are deliberately excluded — both rename with
/// ranker tuning (F82.3), and the F82.6 debug log line remains their one durable-enough record. Only
/// (de)serialize through <see cref="BoothLogPickStampSerializer"/> — the one canonical
/// <see cref="System.Text.Json.JsonSerializerOptions"/> for this shape.
/// </summary>
public sealed record BoothLogPickStamp(IReadOnlyList<BoothLogFiredRuleSummary> FiredRules, bool IsExploration)
{
    /// <summary>
    /// Narrows a <see cref="PersonaPickDiagnostics"/> — the SAME object instance
    /// <c>SegmentRequest.Track.PersonaPick</c> hands the copywriter (SPEC F83.1) — down to this
    /// stamp's shape. An exploration pick's <see cref="PersonaPickDiagnostics.FiredRules"/> is always
    /// empty by the ranker's own contract (F82.4/F83.2 — exploration ignores taste terms entirely),
    /// so this mapping never needs to special-case it.
    /// </summary>
    public static BoothLogPickStamp FromDiagnostics(PersonaPickDiagnostics diagnostics) => new(
        diagnostics.FiredRules.Select(BoothLogFiredRuleSummary.FromTasteRule).ToList(),
        diagnostics.IsExploration);
}
