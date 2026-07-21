namespace GenWave.Core.Domain;

/// <summary>
/// Provenance of a <c>station.persona_taste</c> row (SPEC F82.1, F84.1-F84.3; STORY-213;
/// ARCHITECTURE.md "Personalities on air"). <see cref="Authored"/> rows arrive with the persona card
/// (F79.1, imported/hand-written) and <see cref="Operator"/> rows are a direct operator edit; both are
/// exempt from F84.3's accrual eviction cap. <see cref="Accrued"/> rows are the only ones that cap
/// governs — learned from operator thumbs (F84.1) by a later write path (T70), never by the ranker or
/// feeder (F84.2's structural guarantee).
/// </summary>
public enum PersonaTasteSource
{
    Authored,
    Operator,
    Accrued,
}
