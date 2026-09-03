namespace GenWave.Ads;

/// <summary>
/// A fully format-validated ad script (SPEC F160.3, STORY-390 AC1) — the shape <see
/// cref="AdScriptValidator.Validate"/> hands back on <see cref="AdScriptValidationResult.Accepted"/>.
/// Render (PLAN T401) reads <see cref="Lines"/> directly for its cast-of-voices assembly.
/// </summary>
/// <param name="Lines">Every parsed line, in script order, each carrying its own voice tag.</param>
public sealed record AdScript(IReadOnlyList<AdScriptLine> Lines);
