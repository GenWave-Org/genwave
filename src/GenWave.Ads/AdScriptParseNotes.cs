namespace GenWave.Ads;

/// <summary>
/// The one PUBLIC seam a stored spot's parse notes reach the Host wire through (SPEC F200.3,
/// STORY-467; PLAN T551) — <see cref="AdScriptParser"/> itself stays <see langword="internal"/> (no
/// <c>InternalsVisibleTo</c> widened for GenWave.Host just to reach this one read), so
/// <c>AdsController.ToDto</c> calls <see cref="For"/> instead of the parser directly.
/// </summary>
public static class AdScriptParseNotes
{
    /// <summary>Re-parses <paramref name="script"/> structurally — the SAME
    /// <c>AdScriptParser.Parse(script, int.MaxValue)</c> re-parse <c>AdRenderService</c> already runs
    /// (never a re-validation: the per-line length rule was already enforced at write time) — and
    /// returns its <see cref="AdScript.Notes"/>. Empty, never a throw, when <paramref name="script"/>
    /// is <see langword="null"/> or no longer parses (e.g. hand-edited since it was saved).</summary>
    public static IReadOnlyList<string> For(string? script)
    {
        var parsed = AdScriptParser.Parse(script ?? "", int.MaxValue);
        return parsed is AdScriptValidationResult.Accepted(var parsedScript) ? parsedScript.Notes : [];
    }
}
