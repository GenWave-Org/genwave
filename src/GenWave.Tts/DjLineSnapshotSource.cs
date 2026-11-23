using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;

namespace GenWave.Tts;

/// <summary>
/// Fills a <see cref="TalkSnapshot"/> for one DJ-line attempt from the writer's own inputs (F220.7: the same
/// whatever rules are switched on). Pure: no reads. The only switch-dependent part is the shadow
/// <see cref="CatchphraseLimitsMemo"/>, present exactly when the input carries the legacy result. Card phrases
/// come from the card alone, so they are the same in every mode.
/// </summary>
/// <param name="audiencePosture">The station's audience posture.</param>
/// <param name="taglineLedger">Reads the card's tagline spans; absent means no tagline spans (T713 convention).</param>
public sealed class DjLineSnapshotSource(
    IAudiencePostureProvider audiencePosture,
    ITaglineLedger? taglineLedger = null) : ITalkSnapshotSource<DjLineSnapshotInput>
{
    public ValueTask<TalkSnapshot> BuildAsync(DjLineSnapshotInput input, CancellationToken ct)
    {
        var request = input.Request;

        return ValueTask.FromResult(new TalkSnapshot(
            TalkKinds.DjLine,
            request.Kind.ToString(),
            input.Attempt,
            new TalkList<TalkLine>([new TalkLine(input.PersonaName, SoundTags.Strip(input.CleanedText))]),
            new TalkList<TalkSpeaker>([new TalkSpeaker(input.PersonaId, input.PersonaName, CardPhrasesOf(input))]),
            FactsOf(input.FactBlock),
            input.OwnerMessageCore,
            request.LocalNow,
            input.Timezone,
            TrackOf(request),
            new TalkStation(request.StationName, audiencePosture.Current.ToString()),
            RecentLinesOf(input),
            null,
            MemosOf(input.Legacy)));
    }

    /// <summary>Every row any rule may read, once each (by id), newest first. Each rule picks its own window.</summary>
    static TalkList<TalkRecentLine> RecentLinesOf(DjLineSnapshotInput input) =>
        new(input.NewestRows.Concat(input.TaglineWindowRows).Concat(input.LastHourRows)
            .DistinctBy(e => e.Id)
            .OrderByDescending(e => e.WrittenAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new TalkRecentLine(e.PersonaId, e.Text, e.WrittenAt, e.Fallback)));

    /// <summary>
    /// Every tagline span of the card (a ledger read over no history lists them all, available or not) plus the
    /// card's raw quirk rule texts. Depends on the card only, never on which rules run.
    /// </summary>
    TalkList<string> CardPhrasesOf(DjLineSnapshotInput input)
    {
        var spans = input.Card is { } card && taglineLedger is not null
            ? taglineLedger.Evaluate(card, [], [], input.Request.LocalNow).Select(t => t.Span)
            : [];

        return new TalkList<string>(spans
            .Concat((input.Card?.QuirkRules ?? []).Select(r => r.Text))
            .Distinct(StringComparer.Ordinal));
    }

    static TalkList<string>? FactsOf(string? factBlock)
    {
        if (string.IsNullOrWhiteSpace(factBlock))
            return null;

        return new TalkList<string>(
            factBlock.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The crossing track survives without a current track (title null), as the legacy check passes it regardless.</summary>
    static TalkTrack? TrackOf(SegmentRequest request)
    {
        var crossing = request.CrossingTrackTitle is { } crossingTitle
            ? new TalkTrack(crossingTitle, request.CrossingTrackArtist ?? string.Empty, null, null, null)
            : null;
        return request.Track is { } track
            ? new TalkTrack(track.Title, track.Artist ?? string.Empty, track.Album, track.Year, crossing)
            : crossing is null ? null : new TalkTrack(null, string.Empty, null, null, crossing);
    }

    static TalkMemos MemosOf(LegacyCatchphraseResult? legacy)
    {
        if (legacy is null)
            return TalkMemos.Empty;

        var memo = new CatchphraseLimitsMemo(legacy.Quirks.Lines, legacy.Allowed, legacy.Unavailable, legacy.Spent);
        return TalkMemos.Empty.With(
            CatchphraseLimitsMemo.RuleId,
            JsonSerializer.SerializeToElement(memo, TalkSnapshotJson.Options));
    }
}
