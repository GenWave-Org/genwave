// STORY-566 — Every attempt is recorded (SPEC F220.29 · PLAN T779)
//
// BDD specification — xUnit, no database: the drain's per-row seam is driven with a store that fails on one row.

using GenWave.Core.TalkRules;
using GenWave.MediaLibrary.Station;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTalkCheckDrain
{
    static TalkCheckRecord RecordWithRaw(string raw) =>
        new(
            1,
            TalkCheckMode.Shadow,
            raw,
            null,
            new TalkSnapshot(
                TalkKinds.DjLine, "Banter", 1,
                new TalkList<TalkLine>([new TalkLine("Flip", "Hello.")]),
                new TalkList<TalkSpeaker>([new TalkSpeaker(1, "Flip", TalkList<string>.Empty)]),
                null, null, DateTimeOffset.UnixEpoch, "UTC", null, new TalkStation("GenWave", "friendly"),
                TalkList<TalkRecentLine>.Empty, null, TalkMemos.Empty),
            TalkList<RuleVerdict>.Empty,
            null,
            TalkOutcome.Accepted);

    sealed class FailsOnFirstStore : ITalkCheckStore
    {
        public List<string> Written { get; } = [];

        public Task AppendAsync(TalkCheckRecord record, CancellationToken ct)
        {
            if (record.RawReply == "bad")
            {
                throw new InvalidOperationException("database down");
            }

            Written.Add(record.RawReply);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TalkCheckEntry>> ReadSinceAsync(DateTimeOffset since, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    // Sad path — a write fails
    public sealed class ScenarioOneWriteFails : IAsyncLifetime
    {
        readonly FailsOnFirstStore store = new();
        readonly CapturingLogger<TalkCheckDrainService> logger = new();

        public async Task InitializeAsync()
        {
            var drain = new TalkCheckDrainService(
                System.Threading.Channels.Channel.CreateUnbounded<TalkCheckRecord>().Reader, store, logger);
            await drain.ProcessAsync(RecordWithRaw("bad"), CancellationToken.None);
            await drain.ProcessAsync(RecordWithRaw("good"), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheFailureIsLogged() => Assert.Single(logger.Warnings);

        [Fact]
        public void TheNextRowIsStillWritten() => Assert.Equal(["good"], store.Written);
    }
}
