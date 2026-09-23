namespace GenWave.Ads;

/// <summary>
/// A fully format-validated ad script (SPEC F160.3, STORY-390 AC1) — the shape <see
/// cref="AdScriptValidator.Validate"/> hands back on <see cref="AdScriptValidationResult.Accepted"/>.
/// Render (PLAN T401) reads <see cref="Lines"/> directly for its cast-of-voices assembly.
/// </summary>
/// <param name="Lines">Every parsed line, in script order, each carrying its own voice tag — already
/// folded onto the known cast (SPEC F200.1): a line whose original tag was not <see
/// cref="AdScriptParser.AnnouncerTag"/>/<c>VOICE1</c>/<c>VOICE2</c> is attributed to <see
/// cref="AdScriptParser.AnnouncerTag"/> here, its copy kept verbatim.</param>
/// <param name="Notes">One entry per DISTINCT unknown tag the script carried (SPEC F200.1/F200.3), in
/// first-seen order — <c>"unknown-tag:{TAG}"</c>. Empty when every line's tag was already known. Never
/// a validator failure (STORY-467 AC6): <see cref="AdScriptValidator.Validate"/> returns this
/// <see cref="AdScript"/> unchanged on <see cref="AdScriptValidationResult.Accepted"/>.</param>
public sealed record AdScript(IReadOnlyList<AdScriptLine> Lines, IReadOnlyList<string> Notes);
