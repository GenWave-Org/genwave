namespace GenWave.Context;

using System.Text;

/// <summary>
/// The fencing gate's sanitizing half (T224/T225 review carry-forward; the F1 fence-escape hole closed
/// at T228): neutralizes a provider's fact text BEFORE it can ever reach a prompt or a log line.
/// Provider-authored text is, by definition, sourced from a third party this process does not control
/// — <c>WeatherContextProvider</c>'s Open-Meteo reply today, <c>HistoryContextProvider</c>'s Wikimedia
/// On-This-Day text tomorrow (openly community-editable, i.e. attacker-influenceable) — so every fact
/// string a provider hands back is untrusted input, exactly the class of risk
/// <see cref="Core.Logging.LogSanitize"/> exists for one layer over (log lines rather than prompts).
///
/// <para>
/// <b>Chokepoint placement (the T228 gate's own open question, answered here): <see cref="ContextPipeline"/>,
/// not each provider.</b> Weather already emits single-line facts by construction, and a provider
/// author who remembers to sanitize their own output is exactly the failure mode this exists to
/// remove — a FUTURE provider that forgets costs nothing to write and nothing to review-catch. Calling
/// this from <see cref="ContextPipeline.EnsureFetchedAsync"/>, the ONE place every provider's
/// <see cref="Core.Domain.ContextContent"/> passes through before being cached or handed to a
/// consumer, means no provider — present or future — can bypass it: an implementer cannot forget a
/// step that is not theirs to take. Neither the segment lane (<see cref="ContextPipeline.TickAsync"/>)
/// nor the patter lane (<see cref="ContextPipeline.TryTakeDuePatterFact"/>) does its own sanitizing —
/// both read <see cref="ContextPipeline"/>'s already-sanitized cached content, so this single call site
/// covers every consumer downstream by construction, including <c>LlmPromptBuilder</c>'s own fencing
/// (the OTHER half of this gate, applied on top — belt AND suspenders, not either/or).
/// </para>
///
/// <para>
/// <b>What this strips, and why — TWO passes, not one.</b> A fact is plain spoken text, never markup
/// or a wire format. PASS 1 (<see cref="FlattenControlAndWhitespace"/>): there is no legal reason for
/// a raw control character (newline, carriage return, tab, or any other <see cref="char.IsControl(char)"/>
/// code point) to survive into one — each is replaced with a space rather than deleted outright, so
/// <c>"line one\nline two"</c> becomes <c>"line one line two"</c> (still readable, never silently
/// glued into one word) rather than <c>"line oneline two"</c>. Runs of whitespace — original
/// spaces/tabs and control-character replacements alike — collapse to a single space, and the result
/// is trimmed, so a crafted fact cannot pad itself into faking a delimiter this pipeline or its
/// consumers rely on (<see cref="ContextPipeline"/>'s own <c>" · "</c> segment-window join, a
/// prompt's line boundaries, ...).
/// </para>
///
/// <para>
/// <b>PASS 2, the F1 fix (<see cref="CollapseAngleBracketRuns"/>) — THIS class owns neutralizing
/// <c>&lt;&lt;&lt;</c>/<c>&gt;&gt;&gt;</c>, not <see cref="Tts.LlmPromptBuilder"/>'s fencing.</b> A
/// reviewer-proven payload: a Wikimedia fact whose own text contains a literal <c>&gt;&gt;&gt;</c>
/// closes <c>LlmPromptBuilder</c>'s data fence EARLY, letting whatever follows in that same fact be
/// read as a fresh instruction rather than more fenced data — fencing alone (labeling a span as data)
/// can only delimit correctly if nothing INSIDE the span can forge the delimiter it is being wrapped
/// in; labeling cannot make that true on its own, only neutralizing the input can. THE RULE: every run
/// of 2 or more consecutive, IDENTICAL angle-bracket characters (<c>&lt;</c> or <c>&gt;</c>) collapses
/// to exactly one — so <c>&lt;&lt;&lt;</c> becomes <c>&lt;</c> and <c>&gt;&gt;&gt;</c> becomes
/// <c>&gt;</c>. A sanitized fact can therefore never again contain three identical angle brackets in a
/// row, which makes it structurally impossible for sanitized text to reproduce either fence marker,
/// wherever <c>LlmPromptBuilder</c> chooses to wrap it. A single, isolated <c>&lt;</c> or <c>&gt;</c>
/// (ordinary punctuation — "closed &gt; reopened") is left exactly as it was; only runs of the SAME
/// character, 2 or more long, ever collapse.
/// </para>
/// </summary>
public static class ContextFactSanitizer
{
    /// <summary>Neutralizes <paramref name="value"/> in two passes (see this class's own remarks):
    /// control/whitespace flattening, then angle-bracket run collapsing (the F1 fence-escape fix) — so
    /// the result can never contain a raw control character, a collapsible run of whitespace, or three
    /// identical angle brackets in a row ("&lt;&lt;&lt;"/"&gt;&gt;&gt;"). Never throws on adversarial
    /// input — an all-control-character <paramref name="value"/> collapses to
    /// <see cref="string.Empty"/>, the same "nothing to say" shape a blank fact gets: dropped from the
    /// list entirely by <see cref="ContextPipeline"/>'s own call site rather than surviving as a
    /// phantom entry (see that call site's own remarks).</summary>
    public static string Sanitize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return CollapseAngleBracketRuns(FlattenControlAndWhitespace(value));
    }

    /// <summary>Pass 1: strips control characters (replacing each with a space) and collapses/trims
    /// the resulting whitespace — see this class's own remarks. Runs entirely before
    /// <see cref="CollapseAngleBracketRuns"/>, whose input is this method's own output.</summary>
    static string FlattenControlAndWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var c in value)
        {
            var normalized = char.IsControl(c) ? ' ' : c;

            if (char.IsWhiteSpace(normalized))
            {
                pendingSpace = builder.Length > 0; // Never a LEADING space in the output.
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(normalized);
        }

        return builder.ToString();
    }

    /// <summary>Pass 2, the F1 fence-escape fix (see this class's own remarks): collapses every run of
    /// 2 or more consecutive, identical angle-bracket characters ('&lt;' or '&gt;') down to exactly
    /// one. A lone '&lt;' or '&gt;' (a run of exactly one) is left untouched — collapsing is a no-op
    /// for text that was never trying to fake a fence in the first place.</summary>
    static string CollapseAngleBracketRuns(string value)
    {
        var builder = new StringBuilder(value.Length);
        var runChar = '\0';
        var runLength = 0;

        foreach (var c in value)
        {
            if (c is '<' or '>')
            {
                if (c == runChar)
                {
                    runLength++;
                    continue; // Still inside the same run — nothing appended until it ends.
                }

                FlushRun(builder, runChar, runLength);
                runChar = c;
                runLength = 1;
                continue;
            }

            FlushRun(builder, runChar, runLength);
            runChar = '\0';
            runLength = 0;
            builder.Append(c);
        }

        FlushRun(builder, runChar, runLength);
        return builder.ToString();
    }

    /// <summary>Appends exactly one instance of <paramref name="runChar"/> for any run of one or
    /// more — a no-op for the "no run in progress" sentinel (<paramref name="runLength"/> zero).</summary>
    static void FlushRun(StringBuilder builder, char runChar, int runLength)
    {
        if (runLength > 0)
            builder.Append(runChar);
    }
}
