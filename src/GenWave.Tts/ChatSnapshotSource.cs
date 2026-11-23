using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;

namespace GenWave.Tts;

/// <summary>
/// Fills a <see cref="TalkSnapshot"/> for one crosstalk attempt from the writer's own inputs (F220.7: the same
/// whatever rules are switched on). Pure: no reads, no switch-dependent content, so memos are empty (chat selects no
/// capped quirks, T688).
/// <para>
/// <b>Labels.</b> Each line's <see cref="TalkLine.Speaker"/> is the role tag the parser read: "HOST" or "NEIGHBOR".
/// <b>Speakers.</b> Host first, neighbor second, but only sides whose persona id the caller named: a side without an
/// id has no history and nothing stored, and legacy never checked it, so it has no <see cref="TalkSpeaker"/> (the
/// contract's persona id is not nullable and no placeholder id is invented). <see cref="TalkChatFields"/> carries the
/// role-to-persona ids, so a rule maps a label to a speaker by id and skips a line whose side is absent.
/// </para>
/// <para>
/// Facts, owner message and track are null: the chat writer holds none, and the chat truth checks read only the
/// script text (condition words, frequency, call sign, date claims, the clock from <see cref="TalkSnapshot.LocalNow"/>).
/// </para>
/// </summary>
/// <param name="audiencePosture">The station's audience posture.</param>
/// <param name="taglineLedger">Reads the card's tagline spans; absent means no tagline spans (T713 convention).</param>
public sealed class ChatSnapshotSource(
    IAudiencePostureProvider audiencePosture,
    ITaglineLedger? taglineLedger = null) : ITalkSnapshotSource<ChatSnapshotInput>
{
    public ValueTask<TalkSnapshot> BuildAsync(ChatSnapshotInput input, CancellationToken ct)
    {
        var request = input.Request;

        return ValueTask.FromResult(new TalkSnapshot(
            TalkKinds.Chat,
            CrosstalkLineMemory.SegmentKind,
            input.Attempt,
            new TalkList<TalkLine>(input.Script.Lines.Select(LineOf)),
            SpeakersOf(request),
            null,
            null,
            request.StationLocalNow,
            input.Timezone,
            null,
            new TalkStation(request.StationName, audiencePosture.Current.ToString()),
            TalkRecentLines.Of(input.HostRows.Concat(input.NeighborRows)),
            null,
            TalkMemos.Empty,
            new TalkChatFields(
                input.DurationTargetSeconds, input.MaxLineChars, CrosstalkScriptParser.CharsPerSecond,
                request.HostPersonaId, request.NeighborPersonaId,
                new TalkList<int>(input.Script.Lines.Select(l => l.Text.Length)))));
    }

    static TalkLine LineOf(CrosstalkAiredLine line) =>
        new(CrosstalkScriptParser.DescribeSpeaker(line.Speaker),
            SoundTags.Strip(line.Text));

    TalkList<TalkSpeaker> SpeakersOf(CrosstalkExchangeRequest request)
    {
        var speakers = new List<TalkSpeaker>(2);
        if (request.HostPersonaId is { } hostId)
            speakers.Add(SpeakerOf(hostId, request.HostCard, request.StationLocalNow));
        if (request.NeighborPersonaId is { } neighborId)
            speakers.Add(SpeakerOf(neighborId, request.NeighborCard, request.StationLocalNow));
        return new TalkList<TalkSpeaker>(speakers);
    }

    TalkSpeaker SpeakerOf(long personaId, PersonaCard card, DateTimeOffset localNow) =>
        new(personaId, card.Name, TalkCardPhrases.Of(card, taglineLedger, localNow));
}
