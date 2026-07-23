// gh-#10 (plugin-readiness P1.4) — listener counts as a time series on the event-sink spine.
//
// BDD specification — xUnit. The seam under test is ListenerStatsSampler: ask the SAME
// IListenerStatsSource the spectator surface reads live, publish ListenerCountSampled through
// IStationEventSink — or publish NOTHING when the count is indeterminate (Icecast down), because
// an absent sample is honest and a fabricated zero would poison the series. The BackgroundService
// shell (ListenerStatsPollerService) is deliberately thin and unspecced, mirroring the
// DependencyHealthProbeService/prober split this sampler copies.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Stats;

namespace GenWave.Host.Tests.Specs;

file sealed class ScriptedListenerStatsSource : IListenerStatsSource
{
    public int? Count { get; set; }
    public Exception? Throws { get; set; }

    public Task<int?> GetListenerCountAsync(CancellationToken ct) =>
        Throws is null ? Task.FromResult(Count) : Task.FromException<int?>(Throws);
}

file sealed class RecordingEventSink : IStationEventSink
{
    public List<StationEvent> Published { get; } = [];

    public void Publish(StationEvent evt) => Published.Add(evt);
}

public static class FeatureListenerStatsSampling
{
    static ListenerStatsSampler Sampler(IListenerStatsSource source, IStationEventSink sink) =>
        new(source, sink, NullLogger<ListenerStatsSampler>.Instance);

    public static class ScenarioARealCountBecomesASample
    {
        [Fact]
        public static async Task TheCurrentCountIsPublishedAsAListenerCountSampledEvent()
        {
            var sink = new RecordingEventSink();
            var sampler = Sampler(new ScriptedListenerStatsSource { Count = 7 }, sink);

            await sampler.SampleOnceAsync(CancellationToken.None);

            var sample = Assert.IsType<ListenerCountSampled>(Assert.Single(sink.Published));
            Assert.Equal(7, sample.Listeners);
        }

        [Fact]
        public static async Task ZeroListenersIsARealSampleTooAndIsPublished()
        {
            // Nobody listening is a datum; only INDETERMINATE is skipped.
            var sink = new RecordingEventSink();
            var sampler = Sampler(new ScriptedListenerStatsSource { Count = 0 }, sink);

            await sampler.SampleOnceAsync(CancellationToken.None);

            var sample = Assert.IsType<ListenerCountSampled>(Assert.Single(sink.Published));
            Assert.Equal(0, sample.Listeners);
        }
    }

    public static class ScenarioAnIndeterminateCountPublishesNothing
    {
        [Fact]
        public static async Task ANullCountIsSkippedNotPublishedAsZero()
        {
            var sink = new RecordingEventSink();
            var sampler = Sampler(new ScriptedListenerStatsSource { Count = null }, sink);

            await sampler.SampleOnceAsync(CancellationToken.None);

            Assert.Empty(sink.Published);
        }

        [Fact]
        public static async Task AThrowingSourceIsSkippedAndNeverFaultsThePollLoop()
        {
            // IListenerStatsSource's contract is null-never-throw; the sampler survives a source
            // that breaks it anyway — a failed sample is a skipped sample.
            var sink = new RecordingEventSink();
            var sampler = Sampler(new ScriptedListenerStatsSource { Throws = new InvalidOperationException("boom") }, sink);

            await sampler.SampleOnceAsync(CancellationToken.None);

            Assert.Empty(sink.Published);
        }

        [Fact]
        public static async Task CallerCancellationStillPropagates()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var sampler = Sampler(
                new ScriptedListenerStatsSource { Throws = new OperationCanceledException(cts.Token) },
                new RecordingEventSink());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sampler.SampleOnceAsync(cts.Token));
        }
    }
}
