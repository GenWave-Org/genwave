namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// Everything <see cref="AdScriptWriter"/> needs to write ONE spot script (SPEC F160.1, F160.2,
/// STORY-390 AC2/AC3) — the brief the prompt samples from (<see cref="Brand"/>/<see cref="Premise"/>/
/// <see cref="Tone"/>, already resolved by the caller: T402's own <c>AdSpotWorker</c>, GenWave.Ads,
/// owns picking WHICH enabled <c>ad_brief</c> row to write from — this writer never queries the store
/// itself) plus every live value <c>AdScriptValidator.Validate</c> (GenWave.Ads) also needs
/// (<see cref="Posture"/>/<see cref="MaxLineChars"/>/<see cref="ToleranceRatio"/>, alongside
/// <see cref="SpotSeconds"/>) — the SAME four fields <c>AdScriptValidationRequest</c> carries one
/// project over.
///
/// <para>
/// <b>Why this writer carries validator-shaped fields it never validates with.</b>
/// <c>GenWave.Tts</c> must never reference <c>GenWave.Ads</c> (L10) — the real validator
/// lives there, and reaches this writer only as an opaque
/// <c>Func&lt;string, AdScriptValidationOutcome&gt;</c> delegate the caller closes over. But this
/// writer's OWN prompt (see <see cref="AdScriptPromptBuilder"/>) states the per-line char cap and
/// targets a character budget that lands comfortably inside the validator's own duration ceiling — and
/// its own completion request needs a <c>max_tokens</c> cap wide enough that a script the validator
/// would ACCEPT is never truncated first (see <see cref="AdScriptWriter"/>'s own generation-cap
/// remarks). A caller building this request already has every one of these live values in hand to
/// build its own validate delegate closure; bundling them here means it resolves them once, not twice.
/// </para>
/// </summary>
/// <param name="Brand">The sponsor the spot is for — never a real trademark (the prompt's own demand;
/// SPEC F160.2). Echoed into <see cref="LlmCallRing"/> as this call's subject, mirroring how
/// <c>CrosstalkScriptWriter</c> stamps the host/neighbor persona pairing there.</param>
/// <param name="Premise">The brief's optional premise hint (<c>ad_brief.premise</c>), or
/// <see langword="null"/> when the brief carries none.</param>
/// <param name="Tone">The brief's optional tone hint (<c>ad_brief.tone</c>), or <see langword="null"/>.</param>
/// <param name="SpotSeconds">The spot's target air length — scales the prompt's stated character
/// budget AND (widened by <see cref="ToleranceRatio"/>) the completion's generation cap.</param>
/// <param name="Posture">The live <c>Station:Audience</c> posture (SPEC F95.1) — the SAME value the
/// caller's validate delegate closure gates its own profanity check on; this writer also softens its
/// own prompt's language guidance under <see cref="AudiencePosture.Everyone"/>.</param>
/// <param name="MaxLineChars">The live <c>Llm:MaxCopyChars</c> ceiling — stated directly in the prompt
/// as the per-line char cap (the crosstalk reuse, deliberately not a new knob, SPEC F160.3).</param>
/// <param name="ToleranceRatio">The live <c>Ads:DurationToleranceRatio</c> — widens this writer's own
/// generation cap so a script the validator's duration check would ACCEPT is never truncated by
/// <c>max_tokens</c> first (see <see cref="AdScriptWriter"/>'s own remarks).</param>
public sealed record AdScriptWriteRequest(
    string Brand,
    string? Premise,
    string? Tone,
    int SpotSeconds,
    AudiencePosture Posture,
    int MaxLineChars,
    double ToleranceRatio);
