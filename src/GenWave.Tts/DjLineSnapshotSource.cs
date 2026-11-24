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
            TalkRecentLines.Of(input.NewestRows.Concat(input.TaglineWindowRows).Concat(input.LastHourRows)),
            null,
            MemosOf(input.Legacy),
            Preview: input.Preview));
    }

    TalkList<string> CardPhrasesOf(DjLineSnapshotInput input) =>
        TalkCardPhrases.Of(input.Card, taglineLedger, input.Request.LocalNow);

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
