using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.Ads;

/// <summary>
/// Fills a <see cref="TalkSnapshot"/> for one ad-script attempt from the writer's own inputs (F220.7: the same
/// whatever rules are switched on). Pure: no reads beyond the in-memory duration estimator, no switch-dependent
/// content, so memos are empty.
/// <para>
/// <b>Lines.</b> One per parsed script line. <see cref="TalkLine.Speaker"/> is the folded voice tag ("ANNOUNCER",
/// "VOICE1", "VOICE2"). Text is <see cref="AdScriptWriter.ApplyLineAwareHygiene"/> output with the phone rewrite NOT
/// applied (the phone-number rule judges what the model wrote); the text is exactly what the parser accepted. A reply the ad parser refuses
/// (format) has no lines: format is a fixed check ahead of every rule.
/// </para>
/// <para>
/// <b>Speakers</b> are empty: an ad's cast is voice tags, not personas, and <see cref="TalkSpeaker"/> needs a
/// persona id. <b>Recent lines, facts, owner message and track</b> are empty or null: the legacy ad checks read
/// only the script and the request.
/// </para>
/// <para>
/// <b>Measured lengths.</b> The ad hygiene strips every bracket, so an ad line never holds a sound tag and the
/// length legacy measured is the length of <see cref="TalkSnapshot.Lines"/> text; the legacy phone rewrite ran
/// before measuring and is the one deliberate difference. <see cref="TalkAdFields.ObservedLineSeconds"/> is the
/// estimator's per-line answer, only when grounded in a real measurement, as <see cref="AdScriptValidator"/>
/// uses it.
/// </para>
/// </summary>
/// <param name="durationEstimator">The same estimator the validator is handed.</param>
public sealed class AdSnapshotSource(IPatterDurationEstimator durationEstimator) : ITalkSnapshotSource<AdSnapshotInput>
{
    public ValueTask<TalkSnapshot> BuildAsync(AdSnapshotInput input, CancellationToken ct)
    {
        var request = input.Request;
        var parsed = AdScriptParser.Parse(AdScriptWriter.ApplyLineAwareHygiene(input.RawReply), request.MaxLineChars);
        IReadOnlyList<AdScriptLine> lines = parsed is AdScriptValidationResult.Accepted(var script) ? script.Lines : [];

        return ValueTask.FromResult(new TalkSnapshot(
            TalkKinds.Ad,
            SegmentKind.Ad.ToString(),
            input.Attempt,
            new TalkList<TalkLine>(lines.Select(l => new TalkLine(l.Tag, l.Text))),
            TalkList<TalkSpeaker>.Empty,
            null,
            null,
            input.StationLocalNow,
            input.Timezone,
            null,
            new TalkStation(input.StationName, request.Posture.ToString()),
            TalkList<TalkRecentLine>.Empty,
            new TalkAdFields(
                request.SponsorName ?? string.Empty, request.SponsorPhone, request.SpotSeconds, request.IsPackOwned,
                request.ToleranceRatio, request.MaxLineChars, AdScriptValidator.CharsPerSecond,
                new TalkList<double>(lines.Select(ObservedSeconds))),
            TalkMemos.Empty));
    }

    double ObservedSeconds(AdScriptLine line)
    {
        var estimate = durationEstimator.Estimate(SegmentKind.Ad, line.Tag, line.Tag);
        return estimate.Confidence == PatterEstimateConfidence.Heuristic ? 0 : estimate.Duration.TotalSeconds;
    }
}
