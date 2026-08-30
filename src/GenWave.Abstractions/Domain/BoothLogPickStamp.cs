using System.Text.Json.Serialization;

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

    /// <summary>
    /// The relax step (0–3) the R0→R3 rotation ladder landed on for this pick (SPEC F152.4,
    /// STORY-372, Abstractions 5.5.0) — <see langword="null"/> when no
    /// <see cref="GenWave.Abstractions.Playout.RotationPredicate"/> was in force for the airing at
    /// all, never <c>0</c> for that case (0 means "the predicate was in force and R0 satisfied it
    /// without relaxing"). Deliberately a NON-positional <c>init</c> property so this addition is
    /// byte-for-byte additive over every pre-5.5.0 stamp (STORY-372 AC10): <see cref="JsonIgnoreAttribute"/>
    /// omits it from the wire shape entirely when null (the same per-property opt-in
    /// <see cref="PersonaCard.Taste"/>/<see cref="PersonaCard.Pronunciations"/> use, rather than a
    /// blanket <c>DefaultIgnoreCondition</c> on <see cref="BoothLogPickStampSerializer"/>'s shared
    /// options — this stays a deliberate per-field choice, not an automatic one for whatever field
    /// is added here next), so every existing persisted <c>station.booth_log.pick</c> row stays
    /// byte-identical.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RotationRelax { get; init; }

    /// <summary>
    /// SPEC F151.4 (STORY-371, PLAN T370) — the winning pick's own rotation nudge
    /// (<c>Core.Domain.RotationCandidate.Nudge</c>), stamped ONLY when its magnitude clears the F86
    /// "rotation chip" threshold (<c>|nudge| &gt;= 0.2</c>) — <see langword="null"/> below that
    /// threshold, and for every pick that never reached the persona ranker's rung 0 at all (F151.2).
    /// The threshold is applied at the ONE write site (<c>GenWave.MediaLibrary.Station.BoothLogWriter</c>);
    /// this record itself never re-derives it. Deliberately a NON-positional <c>init</c> property, the
    /// SAME additive convention <see cref="RotationRelax"/> established (STORY-372 AC10) — every
    /// pre-T370 stamp stays byte-identical, and <see cref="JsonIgnoreAttribute"/> omits it from the
    /// wire shape entirely when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Nudge { get; init; }
}
