namespace GenWave.Tts;

using System.Security.Cryptography;
using System.Text;
using GenWave.Core.Domain;

/// <summary>
/// Pure timing math for <see cref="CrosstalkAssembler"/>'s ffmpeg delay/mix plan (SPEC F127.6,
/// STORY-327 AC2) — no I/O, no ffmpeg, no audio: only where each line's render starts on the shared
/// mix timeline. Split out so the jitter/overlap contract ("uniform gaps are the second-biggest
/// TTS-dialogue tell", STORY-327's own scenario remarks) is unit-testable without ever invoking
/// ffmpeg, and reused by <see cref="CrosstalkAssembler"/> unchanged for the real filter graph it
/// builds. Internal — <see cref="GenWave.Tts"/>'s own <c>InternalsVisibleTo</c> to
/// <c>GenWave.Tts.Tests</c> is what lets Story327's facts pin this directly. Named
/// <c>CrosstalkTimeline</c>, not <c>CrosstalkTimingPlanner</c> (T284 review): a LATER task (T285)
/// introduces its own <c>CrosstalkPlanner</c> for casting/scheduling an exchange — a different
/// concern one stage upstream of this one — and the two names read as the same thing at a glance.
/// </summary>
static class CrosstalkTimeline
{
    /// <summary>
    /// Inter-line gap floor (SPEC F127.6, ~0.2-0.8s). Below this a beat between lines reads as an
    /// edit splice rather than a natural conversational pause.
    /// </summary>
    internal const double MinGapSeconds = 0.2;

    /// <summary>
    /// Inter-line gap ceiling (SPEC F127.6, ~0.2-0.8s). Above this the pause reads as the
    /// walkie-talkie "over" beat the whole feature exists to kill.
    /// </summary>
    internal const double MaxGapSeconds = 0.8;

    /// <summary>
    /// How far an interjection line's start rides BACK into the previous line's still-playing tail
    /// (SPEC F127.6) — a single fixed offset, deliberately not jittered: an interjection is a
    /// designed cut-in moment, not ordinary turn-taking, so the gap jitter above already supplies
    /// the "not mechanically uniform" quality this effect needs; jittering it too would only risk
    /// the overlap shrinking near zero on an unlucky draw, at which point it stops reading as an
    /// interruption at all. 0.35s is long enough that both voices are genuinely audible together for
    /// a beat (the two-voices-truly-overlap requirement, not merely a shortened gap) without eating
    /// far enough into the prior line to bury its own final word.
    /// </summary>
    internal const double InterjectionOverlapSeconds = 0.35;

    /// <summary>
    /// Derives a stable per-exchange PRNG seed from the script's own content (SPEC F127.6's own
    /// "inter-line gaps jittered ~0.2–0.8s"; PLAN T284's own "seeded per exchange"): the SAME script
    /// always plans the SAME gaps, so
    /// re-assembling it (a retry, a re-run) reproduces byte-identical timing. Folds every line's
    /// speaker/interjection-flag/text, in order, into one SHA-256 digest — two IDENTICAL scripts
    /// always land on the SAME seed (the actual point); two different scripts overwhelmingly land on
    /// different ones. Never <see cref="Random.Shared"/> unseeded, which would re-roll different
    /// timing on every assembly of the identical script for no reason.
    /// </summary>
    internal static int ComputeSeed(CrosstalkAiredScript script)
    {
        var content = new StringBuilder();
        foreach (var line in script.Lines)
            content.Append(line.Speaker).Append('|').Append(line.IsInterjection).Append('|').Append(line.Text).Append('\n');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString()));
        return BitConverter.ToInt32(hash, 0);
    }

    /// <summary>
    /// <see cref="ComputeSeed(CrosstalkAiredScript)"/>'s own widened sibling, over
    /// <see cref="CastLine"/>s (SPEC F161.2, STORY-391, PLAN T401) — the SAME "identical content plans
    /// identical timing" contract, folding tag/text instead of speaker/interjection/text since a cast
    /// line carries no interjection concept (see <see cref="CastLine"/>'s own remarks).
    /// </summary>
    internal static int ComputeSeed(IReadOnlyList<CastLine> lines)
    {
        var content = new StringBuilder();
        foreach (var line in lines)
            content.Append(line.Tag).Append('|').Append(line.Text).Append('\n');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString()));
        return BitConverter.ToInt32(hash, 0);
    }

    /// <summary>
    /// One jittered gap per line TRANSITION (<paramref name="transitionCount"/> = line count - 1,
    /// SPEC F127.6), each in [<see cref="MinGapSeconds"/>, <see cref="MaxGapSeconds"/>], drawn in
    /// order from one <paramref name="seed"/>-derived <see cref="Random"/> — so the SAME script
    /// always plans the SAME sequence (see <see cref="ComputeSeed"/>). A transition landing on an
    /// interjection line still draws a value here (<see cref="ComputeLineStartSeconds"/> simply
    /// never uses it, reaching for <see cref="InterjectionOverlapSeconds"/> instead) so that marking
    /// one line as an interjection never reshuffles every LATER transition's own jitter.
    /// </summary>
    internal static IReadOnlyList<double> ComputeGapsSeconds(int transitionCount, int seed)
    {
        var rng = new Random(seed);
        var gaps = new double[transitionCount];
        for (var i = 0; i < transitionCount; i++)
            gaps[i] = MinGapSeconds + (rng.NextDouble() * (MaxGapSeconds - MinGapSeconds));

        return gaps;
    }

    /// <summary>
    /// The absolute start time (seconds, from the assembled mix's own t=0) for a line given where
    /// the line immediately before it ends: an interjection starts BEFORE that tail
    /// (<see cref="InterjectionOverlapSeconds"/> earlier, floored at zero — ffmpeg's own
    /// <c>adelay</c> cannot accept a negative delay), an ordinary line starts AFTER it, offset by its
    /// own jittered <paramref name="gapSeconds"/> (see <see cref="ComputeGapsSeconds"/>).
    /// </summary>
    internal static double ComputeLineStartSeconds(double previousLineEndSeconds, bool isInterjection, double gapSeconds) =>
        isInterjection
            ? Math.Max(0.0, previousLineEndSeconds - InterjectionOverlapSeconds)
            : previousLineEndSeconds + gapSeconds;
}
