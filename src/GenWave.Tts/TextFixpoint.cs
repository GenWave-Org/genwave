namespace GenWave.Tts;

/// <summary>
/// The bounded "repeat one pass until nothing changes" loop shared by every strip pass in this
/// assembly whose single regex application only ever resolves the OUTERMOST or INNERMOST level of
/// a nestable token — <see cref="SpeechText"/>'s think-block stripper and
/// <see cref="PiperSpeechMarkup"/>'s bracket-token stripper both peel nested tokens one level per
/// pass this way (F68.3, F96.3); one shared implementation is what keeps the two from drifting
/// apart on the cap or the fixpoint condition itself.
///
/// <para>
/// Capped at <paramref name="maxPasses"/> so a pathological input can never spin the render/apply
/// path (real markup or reasoning-tag nesting never goes anywhere near that deep) — the identical
/// "never stall the render path" discipline <see cref="LiteralRegexPosture"/>'s match timeout
/// applies to literal-regex rule sets. A <paramref name="pass"/> that cannot safely re-run (e.g. a
/// regex whose own match attempt just timed out) is expected to return its input UNCHANGED rather
/// than throw past this helper — doing so ends the loop on the very next comparison exactly as a
/// genuine fixpoint would, letting the caller fall through with whatever the last successful pass
/// already produced instead of losing that progress to a propagating exception.
/// </para>
/// </summary>
internal static class TextFixpoint
{
    public static string Apply(string text, int maxPasses, Func<string, string> pass)
    {
        var current = text;
        var iterations = 0;
        string before;
        do
        {
            before = current;
            current = pass(before);
            iterations++;
        }
        while (current != before && iterations < maxPasses);

        return current;
    }
}
