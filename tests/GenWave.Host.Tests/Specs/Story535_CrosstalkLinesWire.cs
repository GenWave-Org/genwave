// STORY-535 AC1/AC2 wire proof — crosstalk lines through the real container (SPEC F218.14 · PLAN T694)
//
// Boots the real composition root (Program.cs, no DB) against a stub completions endpoint, resolves the
// production CrosstalkScriptWriter (so its IRepetitionCheck / IUtteranceStore / IStationEventSink come
// from DI) and writes one exchange for two personas. Only the utterance store and the booth-log
// appender (the two Postgres edges) are faked.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Tts;
using Microsoft.Extensions.DependencyInjection;

namespace GenWave.Host.Tests.Specs;

file sealed class LineStore(IReadOnlyList<UtteranceEntry> seeded) : IUtteranceStore
{
    public List<NewUtterance> Recorded { get; } = [];

    public Task<long> RecordAsync(NewUtterance utterance, CancellationToken ct)
    {
        lock (Recorded) Recorded.Add(utterance);
        return Task.FromResult((long)Recorded.Count);
    }

    public Task MarkAiredAsync(long id, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<UtteranceEntry>> RecentAsync(long personaId, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UtteranceEntry>>(seeded.Where(e => e.PersonaId == personaId).Take(take).ToList());

    public Task<IReadOnlyList<UtteranceEntry>> SinceAsync(long personaId, DateTimeOffset since, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UtteranceEntry>>([]);

    public Task<int> SweepAsync(CancellationToken ct) => Task.FromResult(0);
}

public static class FeatureCrosstalkLinesWire
{
    const long HostId = 7;
    const long NeighborId = 8;

    const string Reply =
        "HOST: Welcome back everybody, the lights are low and the records are warm.\n" +
        "NEIGHBOR: Always a pleasure to sit beside you on a night like this.\n" +
        "HOST: Let me tell you about the odd little shop around the corner.\n" +
        "NEIGHBOR: Funny, I walked past that very place on my way in.";

    static PersonaCard CardNamed(string name) =>
        new(PersonaCard.CurrentSchemaVersion, name, "", "Soul line.", [],
            new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], []);

    public sealed record ExchangeOutcome(
        CrosstalkWriteResult Result, int LlmCalls, IReadOnlyList<NewUtterance> Stored);

    /// <summary>Boots the container and writes one exchange, so every fact reads the same run.</summary>
    public abstract class ExchangeRun : IAsyncLifetime
    {
        LlmCompletionsStub? stub;
        RepeatingDjFactory? factory;
        ExchangeOutcome? outcome;

        /// <summary>What the stored histories already say, seeded per persona.</summary>
        protected abstract IReadOnlyList<UtteranceEntry> Seeded { get; }

        public ExchangeOutcome Outcome =>
            outcome ?? throw new InvalidOperationException("The fixture has not written yet.");

        public async Task InitializeAsync()
        {
            stub = await LlmCompletionsStub.StartAsync();
            stub.ReplyContent = Reply;
            var store = new LineStore(Seeded);
            factory = new RepeatingDjFactory(stub.BaseUri.ToString(), store, new FakeBoothLogAppender());

            var request = new CrosstalkExchangeRequest(
                CardNamed("Nova"), CardNamed("Rex"), "GenWave", ShowName: "Night Shift", Daypart: null,
                StationLocalNow: new DateTimeOffset(2026, 11, 9, 22, 0, 0, TimeSpan.Zero),
                HostPersonaId: HostId, NeighborPersonaId: NeighborId, ShowId: 3);

            var result = await factory.Services.GetRequiredService<CrosstalkScriptWriter>()
                .WriteExchangeAsync(request, CancellationToken.None);
            outcome = new ExchangeOutcome(result, stub.Requests.Count, store.Recorded.ToList());
        }

        public async Task DisposeAsync()
        {
            if (factory is not null) await factory.DisposeAsync();
            if (stub is not null) await stub.DisposeAsync();
        }
    }

    public sealed class FreshExchangeRun : ExchangeRun
    {
        protected override IReadOnlyList<UtteranceEntry> Seeded => [];
    }

    /// <summary>The host has already said its second line, so the production check must catch it on both calls.</summary>
    public sealed class RepeatingExchangeRun : ExchangeRun
    {
        protected override IReadOnlyList<UtteranceEntry> Seeded =>
        [
            new UtteranceEntry(
                1, HostId, null, UtteranceKind.Patter, "LeadIn", ShiftKey.Unscheduled(HostId, new DateOnly(2026, 11, 9)),
                "Now let me tell you about the odd little shop around the corner today.", false, DateTimeOffset.UnixEpoch, null),
        ];
    }

    public sealed class ScenarioAnAcceptedExchangeIsStoredLineByLine(FreshExchangeRun run) : IClassFixture<FreshExchangeRun>
    {
        [Fact]
        public void FourRowsAreRecorded() => Assert.Equal(4, run.Outcome.Stored.Count);

        [Fact]
        public void EveryRowIsKindCrosstalk() =>
            Assert.Equal(Enumerable.Repeat(UtteranceKind.Crosstalk, 4), run.Outcome.Stored.Select(u => u.Kind));

        [Fact]
        public void EveryRowIsSegmentKindCrosstalkLine() =>
            Assert.Equal(Enumerable.Repeat("CrosstalkLine", 4), run.Outcome.Stored.Select(u => u.SegmentKind));

        [Fact]
        public void TwoRowsBelongToEachPersona() =>
            Assert.Equal(
                [(HostId, 2), (NeighborId, 2)],
                run.Outcome.Stored.GroupBy(u => u.PersonaId).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())));
    }

    public sealed class ScenarioTheProductionCheckReAsksOnceThenDropsTheStubbornLine(RepeatingExchangeRun run)
        : IClassFixture<RepeatingExchangeRun>
    {
        [Fact]
        public void TheStubLlmWasCalledExactlyTwice() => Assert.Equal(2, run.Outcome.LlmCalls);

        [Fact]
        public void TheHostsRepeatedLineIsNotStored() =>
            Assert.Equal(
                [(HostId, 1), (NeighborId, 2)],
                run.Outcome.Stored.GroupBy(u => u.PersonaId).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())));
    }
}
