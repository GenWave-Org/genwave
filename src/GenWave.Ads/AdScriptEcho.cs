namespace GenWave.Ads;

/// <summary>
/// Bounds an untrusted string before it reaches a validation violation's <c>Reason</c> — the ONE place
/// the CWE-117 log-forging discipline lives, shared by <see cref="AdScriptParser"/> (its own tag
/// echoes) and <see cref="AdScriptValidator"/> (its own stage-direction echo), rather
/// than each duplicating the same const and one-liner (PLAN T552 review N1). Every violation Reason is
/// logged and surfaced verbatim (STORY-390 AC9's 400), so an untrusted script's own text must never
/// reach it unbounded.
/// </summary>
internal static class AdScriptEcho
{
    /// <summary>Cap for a value echoed into a violation Reason (the original
    /// <c>AdScriptParser.MaxEchoedChars</c>/<c>AdScriptValidator.MaxEchoedChars</c> precedent, PLAN
    /// T399 review F6, CWE-117 log forging).</summary>
    const int MaxEchoedChars = 120;

    /// <summary>Truncates <paramref name="text"/> to <see cref="MaxEchoedChars"/>, appending an
    /// ellipsis when it was cut, so a violation Reason never carries an unbounded echo of untrusted
    /// text. Bounds LENGTH only — no control-character strip: <see cref="AdScriptParser"/>'s own call
    /// sites pass a tag that already matched <c>TagPattern</c> (<c>^[A-Z][A-Z0-9]*$</c>, which admits no
    /// control character), and <see cref="AdScriptValidator"/>'s own call sites echo a regex-matched
    /// run (a phone-shaped digit group, a stage-direction shape) rather than a whole raw line — a future
    /// call site that would echo genuinely unvalidated freeform text into a Reason must add its own
    /// control-character strip first.</summary>
    public static string ForReason(string text) => text.Length <= MaxEchoedChars ? text : text[..MaxEchoedChars] + "…";
}
