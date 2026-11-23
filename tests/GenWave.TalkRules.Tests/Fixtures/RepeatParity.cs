using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Memory;
using GenWave.Tts;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>
/// Runs repeat fixtures through today's repeat paths (<c>LlmCopyWriter.FindRepeats</c> for a DJ line,
/// <c>CrosstalkLineMemory</c> for a chat) and through the rule on a snapshot the real source built from the same rows.
/// </summary>
internal static class RepeatParity
{
    public const long HostId = 7;
    public const long NeighborId = 8;

    static readonly DateTimeOffset Now = TalkSnapshots.DjLine(string.Empty).LocalNow;
    static readonly TaglineLedger Ledger = new();

    /// <summary>
    /// The phrases the DJ-line shared-run check names for each case (empty when it names none), then the rule's.
    /// Compares what is named, not only whether anything is.
    /// </summary>
    public static (List<string> Legacy, List<string> Rule) DjVerdicts(IJudgingTalkRule rule, IReadOnlyList<RepeatParityCase> cases) =>
        ([.. cases.Select(LegacyDjNamed)], [.. cases.Select(c => NamedBy(rule, DjSnapshot(c)))]);

    /// <summary>The phrases the chat repeat check names for each case across the whole reply, then the rule's.</summary>
    public static (List<string> Legacy, List<string> Rule) ChatVerdicts(IJudgingTalkRule rule, IReadOnlyList<ChatRepeatCase> cases) =>
        ([.. cases.Select(LegacyChatNamed)], [.. cases.Select(c => NamedBy(rule, ChatSnapshot(c)))]);

    static string NamedBy(IJudgingTalkRule rule, TalkSnapshot snapshot) => rule.Judge(snapshot)?.Evidence ?? string.Empty;

    public static TalkSnapshot DjSnapshot(RepeatParityCase c, bool preview = false)
    {
        var rows = Rows(HostId, c.Recent);
        var input = new DjLineSnapshotInput(
            Request(c), 1, c.Copy, HostId, c.PersonaName, CardOf(c.PersonaName, c.Tagline), null, null, "America/Edmonton",
            [.. rows.Take(10)], rows, [], Preview: preview);
        return new DjLineSnapshotSource(new FixedAudiencePosture(), Ledger).BuildAsync(input, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
    }

    public static TalkSnapshot ChatSnapshot(ChatRepeatCase c)
    {
        var store = StoreOf(c);
        var request = ChatRequest(c);
        var input = new ChatSnapshotInput(
            request, 1, ScriptOf(c), "America/Edmonton", 600, 400,
            store.RecentAsync(HostId, 10, CancellationToken.None).GetAwaiter().GetResult().ToList(),
            store.RecentAsync(NeighborId, 10, CancellationToken.None).GetAwaiter().GetResult().ToList());
        return new ChatSnapshotSource(new FixedAudiencePosture(), Ledger).BuildAsync(input, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// The DJ-line check as the writer calls it (the store's newest ten rows, the ledger's tagline statuses), as a bool.
    /// </summary>
    public static bool LegacyDjFlags(RepeatParityCase c) => LegacyDjHits(c).Count > 0;

    /// <summary>
    /// What today's check names that the rule judges: the shared-run phrases. The named hit per unavailable tagline
    /// (<c>catchphrase-limits</c>, T785) is dropped. A spent-quirk hit is too, but the writer is given no quirk selection here, so none exist.
    /// </summary>
    static string LegacyDjNamed(RepeatParityCase c) => string.Join(", ", LegacyDjHits(c).Distinct(StringComparer.Ordinal));

    static List<string> LegacyDjHits(RepeatParityCase c)
    {
        var newest = Rows(HostId, c.Recent).Take(10).ToList();
        var card = CardOf(c.PersonaName, c.Tagline);
        var taglines = Ledger.Evaluate(card, newest, [], Now);
        var unavailable = taglines.Where(t => !t.Available).Select(t => t.Span).ToHashSet(StringComparer.Ordinal);
        return [.. LlmCopyWriter.FindRepeats(new RepetitionCheck(), c.Copy, Request(c), c.PersonaName, newest, card, null, taglines)
            .Where(phrase => !unavailable.Contains(phrase))];
    }

    static string LegacyChatNamed(ChatRepeatCase c)
    {
        var memory = new CrosstalkLineMemory(new RepetitionCheck(), StoreOf(c), NoOpStationEventSink.Instance, NullLogger.Instance);
        var histories = memory.ReadHistoriesAsync(ChatRequest(c), CancellationToken.None).GetAwaiter().GetResult();
        return string.Join(", ", memory.FindRepeats(ScriptOf(c), "GenWave", histories)
            .SelectMany(hit => hit.Phrases).Distinct(StringComparer.Ordinal));
    }

    static SegmentRequest Request(RepeatParityCase c) =>
        new(SegmentKind.LeadIn, "af_heart", c.StationName,
            c.TrackTitle is null
                ? null
                : new MediaItem("m1", "/media/a.mp3", c.TrackTitle, default, c.TrackArtist, Album: c.TrackAlbum, Year: c.TrackYear),
            Now, "test-station")
        {
            CrossingTrackTitle = c.CrossingTitle,
            CrossingTrackArtist = c.CrossingArtist,
        };

    static CrosstalkExchangeRequest ChatRequest(ChatRepeatCase c) =>
        new(CardOf("Flip", c.HostTagline), CardOf("Tony", ""), "GenWave", null, null, Now, HostId, NeighborId);

    static CrosstalkAiredScript ScriptOf(ChatRepeatCase c) => new([.. c.Lines.Select((text, index) =>
        new CrosstalkAiredLine(index % 2 == 0 ? CrosstalkSpeaker.Host : CrosstalkSpeaker.Neighbor, text, false))]);

    static SpokenRowsStore StoreOf(ChatRepeatCase c) =>
        new([.. Rows(HostId, c.HostRecent), .. Rows(NeighborId, c.NeighborRecent)]);

    static PersonaCard CardOf(string name, string tagline) =>
        new(PersonaCard.CurrentSchemaVersion, name, tagline, "", [], new VoiceSpec("", "af_nova", 1.0, "en"), 0, [], []);

    /// <summary>Stored rows, newest first, two minutes apart, so a tagline used a few rows back is still inside the hour.</summary>
    static List<UtteranceEntry> Rows(long personaId, IReadOnlyList<RecentSaid> said) =>
        [.. said.Select((line, index) => new UtteranceEntry(
            personaId * 1000 - index, personaId, null, UtteranceKind.Patter, "LeadIn",
            ShiftKey.Unscheduled(personaId, DateOnly.FromDateTime(Now.DateTime)),
            line.Text, line.Fallback, Now.AddMinutes(-2 * (index + 1)), null))];
}
