using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>Runs one DJ-line input through today's truth gate and through the rules on a snapshot the real source built.</summary>
internal static class DjParity
{
    static readonly DateTimeOffset Now = TalkSnapshots.DjLine(string.Empty).LocalNow;

    sealed class FixedPosture : IAudiencePostureProvider
    {
        public AudiencePosture Current => AudiencePosture.Everyone;
    }

    /// <summary>Whether <c>LlmCopyWriter.CheckTruthGate</c> rejects each case, then whether the rule flags it.</summary>
    public static (List<bool> Legacy, List<bool> Rule) Verdicts(IJudgingTalkRule rule, IReadOnlyList<DjParityCase> cases) =>
        ([.. cases.Select(LegacyRejects)], [.. cases.Select(c => rule.Judge(SnapshotOf(c)) is not null)]);

    static bool LegacyRejects(DjParityCase c) =>
        !LlmCopyWriter.CheckTruthGate(c.Copy, c.FactBlock, Now, c.Track, "GenWave", c.Owner).Passed;

    static TalkSnapshot SnapshotOf(DjParityCase c)
    {
        var track = c.Track is null
            ? null
            : new MediaItem("m1", "/media/a.mp3", c.Track, default, "Artist A");
        var request = new SegmentRequest(SegmentKind.LeadIn, "af_heart", "GenWave", track, Now, "test-station")
        {
            CrossingTrackTitle = c.Crossing,
            CrossingTrackArtist = c.Crossing is null ? null : "Artist B",
        };
        var input = new DjLineSnapshotInput(
            request, 1, c.Copy, 7, "Flip", null, c.FactBlock, c.Owner, "America/Edmonton", [], [], []);
        return new DjLineSnapshotSource(new FixedPosture()).BuildAsync(input, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
    }
}
