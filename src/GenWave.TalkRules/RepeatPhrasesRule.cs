using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Memory;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>
/// No repeated phrases. Judges each line against its own speaker's recent lines with <see cref="RepetitionCheck"/>'s shared run,
/// as <c>LlmCopyWriter.FindRepeats</c> (DJ line) and <c>CrosstalkLineMemory.FindRepeats</c> (chat) do today.
/// <list type="bullet">
/// <item>Recent lines: the speaker's newest rows (<see cref="LlmCopyWriter.RepetitionWindow"/> for a DJ line,
/// <see cref="CrosstalkLineMemory.Window"/> for a chat), then the template (fallback) rows dropped.</item>
/// <item>On a DJ line, card phrases (tagline spans and raw quirk rule texts) are always skipped, as allowed phrases. Legacy skips tagline spans only,
/// so a repeated quirk phrase is flagged today and not here (PLAN T784; the shadow report must explain it).</item>
/// <item>In a chat no card phrase is skipped, matching <c>CrosstalkLineMemory.FindRepeats</c>, which passes no allowed phrases: chats have no
/// tagline limiter, so a speaker repeating their own tagline across recent chats is flagged, as today (Dean 2026-11-23).</item>
/// <item>The spent-quirk and over-quota halves of the legacy check belong to <c>catchphrase-limits</c>, not here.</item>
/// <item>A preview render is never judged: legacy runs the repeat check on air only.</item>
/// </list>
/// </summary>
public sealed class RepeatPhrasesRule(IRepetitionCheck sharedRun) : IJudgingTalkRule
{
    public string Id => "repeat-phrases";

    public TalkKinds CanCover => TalkKinds.DjLine | TalkKinds.Chat;

    public TalkKinds OnByDefault => TalkKinds.DjLine | TalkKinds.Chat;

    public TalkRuleDescription Describe() => new(
        "No repeated phrases.",
        $"Flags a line that repeats a run of {RepetitionCheck.MinRunWords} or more words in a row from one of that speaker's last {LlmCopyWriter.RepetitionWindow} lines (a chat looks at their last {CrosstalkLineMemory.Window}). A line that was a stand-in template is not counted, and neither are the speaker's and station's names, or the track details. On a single-DJ line the speaker's own catchphrases are skipped too, but in a chat they are not.",
        "Hearing the same sentence again and again makes the station sound like a loop.",
        "The speaker said \"that was a really great song\" earlier, and the new line says it again.");

    public RuleFlag? Judge(TalkSnapshot after)
    {
        if (after.Preview || after.Kind is not (TalkKinds.DjLine or TalkKinds.Chat))
            return null;

        var repeated = after.Lines
            .SelectMany(line => SpeakerOf(after, line) is { } speaker ? RepeatedRuns(after, line, speaker) : [])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return repeated.Count == 0
            ? null
            : new RuleFlag(
                "The line repeats words the speaker said recently.",
                $"Say something new instead of {string.Join(" and ", repeated.Select(run => $"\"{run}\""))}.",
                string.Join(", ", repeated));
    }

    IEnumerable<string> RepeatedRuns(TalkSnapshot after, TalkLine line, TalkSpeaker speaker)
    {
        var window = after.Kind == TalkKinds.Chat ? CrosstalkLineMemory.Window : LlmCopyWriter.RepetitionWindow;
        var recent = after.RecentLines
            .Where(row => row.PersonaId == speaker.PersonaId)
            .Take(window)
            .Where(row => !row.Fallback)
            .Select(row => row.Text)
            .ToList();
        var track = after.Track;
        var context = new RepetitionContext(
            recent, [], speaker.Name, after.Station.Name,
            track?.Title, track?.Artist, track?.Album, track?.Year,
            track?.Crossing?.Title, track?.Crossing?.Artist,
            AllowedPhrases: after.Kind == TalkKinds.Chat ? [] : [.. speaker.CardPhrases]);
        return sharedRun.Check(SoundTags.Strip(line.Text), context).Hits.Select(hit => hit.Phrase);
    }

    /// <summary>A DJ line has one speaker. A chat line maps its role label to a side's persona; a side with no persona id is not judged, as legacy skips it.</summary>
    static TalkSpeaker? SpeakerOf(TalkSnapshot after, TalkLine line)
    {
        if (after.Kind == TalkKinds.DjLine)
            return after.Speakers.FirstOrDefault();

        var personaId = line.Speaker switch
        {
            "HOST" => after.Chat?.HostPersonaId,
            "NEIGHBOR" => after.Chat?.NeighborPersonaId,
            _ => null,
        };
        return after.Speakers.FirstOrDefault(speaker => speaker.PersonaId == personaId);
    }
}
