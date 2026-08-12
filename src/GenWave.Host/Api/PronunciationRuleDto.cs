namespace GenWave.Host.Api;

/// <summary>
/// One row of <c>GET /api/pronunciations</c> (SPEC F97.3, F100.3, STORY-254): the merged
/// station∪persona pronunciation-rule view, one row per rule from EITHER source.
///
/// <list type="bullet">
///   <item><see cref="Source"/> is <c>"station"</c> or <c>"persona"</c> — which SPEC F97.3 side
///   supplied the rule.</item>
///   <item><see cref="InEffect"/> is <see langword="false"/> for a station rule shadowed by an
///   identical-identity persona rule (F97.4), or for a rule that never compiled at all (see
///   <see cref="Reason"/>) — still listed, visibly not the one firing.</item>
///   <item>A row is content-addressed by (<see cref="Pattern"/>, <see cref="Word"/>) — case-
///   insensitive, the same identity the merge and the hit-count store both key on
///   (<c>PUT</c>/<c>DELETE /api/pronunciations?pattern=&amp;word=</c>). There is no positional id:
///   the underlying <c>Tts:Pronunciations</c> array has no stable index across a save (T144 review
///   finding F1/F2), so uniqueness of (Pattern, Word) among station rows is enforced at write time
///   instead — a colliding <c>POST</c>/<c>PUT</c> is refused with 409, never silently committed
///   twice.</item>
///   <item><see cref="HitCount"/> is <see langword="null"/> for a rule that has never fired OR is
///   not <see cref="InEffect"/> — a shadowed or never-compiled rule can never itself have fired,
///   and the hit-count store keys purely on (pattern, word) with no source provenance of its own
///   (T142 review ruling), so a count is only ever attached to the row actually in effect.</item>
///   <item><see cref="Reason"/> is non-null only for a declared STATION rule
///   <c>PronunciationRuleSet.Create</c> silently drops at compile time (a blank pattern/word/ipa, an
///   ipa carrying <c>)</c>/<c>[</c>/<c>]</c>, a word not found inside its own pattern) — named here so
///   the operator never sees an empty list over a non-empty <c>Tts:Pronunciations</c> setting (T144
///   review finding F3). Such a row is still addressable and deletable through this same content
///   identity.</item>
/// </list>
/// </summary>
public sealed record PronunciationRuleDto(
    string Pattern, string Word, string Ipa, string Source, bool InEffect, long? HitCount, string? Reason);
