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
public sealed record AdScriptValidationRequest(
    AudiencePosture Posture,
    int MaxLineChars,
    int SpotSeconds,
    double ToleranceRatio);
