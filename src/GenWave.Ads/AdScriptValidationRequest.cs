using GenWave.Core.Domain;

namespace GenWave.Ads;

/// <summary>
/// The live values <see cref="AdScriptValidator.Validate"/> needs but never resolves itself (PLAN
/// T399 design): the validator stays pure and deterministic per call — every caller (T400's writer,
/// T403's owner-save endpoint) resolves its own current <c>Station:Audience</c>/<c>Llm:MaxCopyChars</c>/
/// <c>Ads:DurationToleranceRatio</c> values and hands them in fresh, rather than the validator reading
/// live options itself.
/// </summary>
/// <param name="Posture">The live <c>Station:Audience</c> posture (SPEC F95.1) — gates the audience-posture
/// check (skipped entirely under <see cref="AudiencePosture.Mature"/>).</param>
/// <param name="MaxLineChars">The live <c>Llm:MaxCopyChars</c> ceiling — the SAME per-line budget
/// ordinary blurbs and crosstalk lines carry, deliberately not a new knob (SPEC F160.3).</param>
/// <param name="SpotSeconds">The spot's target air length (<c>ad_spot.spot_seconds</c>).</param>
/// <param name="ToleranceRatio">The live <c>Ads:DurationToleranceRatio</c> (default 0.4) — the script's
/// estimated read time may run up to <c>SpotSeconds * (1 + ToleranceRatio)</c> before it refuses.</param>
/// <param name="SponsorName">The sponsor's own literal display name, or <see langword="null"/> when
/// none is known. For an owner sponsor (<see cref="IsPackOwned"/> <see langword="false"/>) with a
/// non-blank value here, <see cref="AdScriptValidator"/>'s brand-blocklist check (SPEC F172.5) treats
/// this EXACT literal — folded, nothing fuzzy — as allowed text; any OTHER blocklisted name in the
/// script still refuses, and a pack-owned sponsor never gets this skip no matter what this field
/// carries.</param>
/// <param name="SponsorPhone">The sponsor's own literal phone number, or <see langword="null"/>. For
/// an owner sponsor with a non-blank value here, <see cref="AdScriptValidator"/>'s 555 rule (SPEC
/// F172.5) treats a phone-shaped run whose DIGITS equal this value's digits as allowed — the
/// comparison is formatting-insensitive (punctuation stripped from both sides), the surrounding script
/// text is still read raw. Every OTHER non-555 phone-shaped run still refuses.</param>
/// <param name="IsPackOwned">Whether this script belongs to a pack-owned sponsor — the parody posture
/// SPEC F172.5 keeps unchanged for packs (neither skip above ever runs). Defaults <see
/// langword="true"/> (PLAN T438 ruling: fail closed, not fail open) so every caller that predates
/// these three members keeps today's strict posture without changing a single call site.</param>
public sealed record AdScriptValidationRequest(
    AudiencePosture Posture,
    int MaxLineChars,
    int SpotSeconds,
    double ToleranceRatio,
    string? SponsorName = null,
    string? SponsorPhone = null,
    bool IsPackOwned = true);
