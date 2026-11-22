// STORY-554 — A render's time cap follows the engine's live render budget (SPEC F219.6 · PLAN T771a)
//
// BDD specification — xUnit. The speech client no longer cuts every request at the 100 s HttpClient default. Each render
// gets its own cap: twice the engine's limit, never under 100 s, read when the render starts. A late finish inside the cap
// is still seen and judged late. Ping and voices keep a 100 s cap.

using System.Net;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureRenderCapFollowsTheBudget
{
    static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    static string Override(string engine, int seconds) => $"{{\"{engine}\":{seconds}}}";

    // A real client over a scripted engine and a fake clock; the budget is the engine's own unless overridden live.
    sealed class Rig : IDisposable
    {
        const string Host = "chatterbox";
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), "gw-t771a-" + Guid.NewGuid().ToString("N"));
        readonly ScriptedSpeechEngineHandler engines = new();
        readonly HttpClient http;
        readonly TestOptionsMonitor<TtsOptions> options;

        public Rig(int engineBudgetSeconds)
        {
            http = new HttpClient(engines) { Timeout = Timeout.InfiniteTimeSpan };
            options = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
            Engine = new VoiceEngine("chatterbox", "http://chatterbox:8004", VoiceEngineProfiles.Chatterbox, engineBudgetSeconds);
            Client = new SpeechEngineClient(http, options, Activity, timeProvider: Time);
        }

        public FakeTimeProvider Time { get; } = new();

        public EngineRenderActivity Activity { get; } = new();

        public VoiceEngine Engine { get; }

        public SpeechEngineClient Client { get; }

        public string? LastFailure => Activity.Read(Engine.Identity).Failure;

        public void SetBudgetOverride(int seconds) =>
            options.CurrentValue = new TtsOptions { CacheRoot = cacheRoot, Format = "wav", EngineRenderBudgets = Override(Engine.Name, seconds) };

        /// <summary>One render held open by the engine, the clock moved on by <paramref name="after"/>, then the engine answers (unless the cap already cut it).</summary>
        public async Task<(string? Path, Exception? Error)> RenderTakingAsync(TimeSpan after)
        {
            var began = engines.HoldNextRender(Host);
            var render = Client.RenderOnAsync(Engine, new TtsRenderContext("Back to the music.", "reader-1", Kind: null), default);
            await began.WaitAsync(Guard);
            Time.Advance(after);
            engines.Finish(Host, HttpStatusCode.OK);
            try
            {
                return (await render.WaitAsync(Guard), null);
            }
            catch (OperationCanceledException ex)
            {
                return (null, ex);
            }
        }

        public void Dispose()
        {
            http.Dispose();
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    // Happy path: the chatterbox budget (120 s) is longer than the 100 s default that used to cut it.
    public sealed class ScenarioARenderTakesLongerThanTheOldDefault : IAsyncLifetime
    {
        readonly Rig rig = new(engineBudgetSeconds: 120);
        (string? Path, Exception? Error) outcome;

        public async Task InitializeAsync() => outcome = await rig.RenderTakingAsync(TimeSpan.FromSeconds(110));

        public Task DisposeAsync()
        {
            rig.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheRenderCompletes() => Assert.Null(outcome.Error);

        [Fact]
        public void TheFileIsReturned() => Assert.True(File.Exists(outcome.Path));

        [Fact]
        public void TheEngineIsNotFlagged() => Assert.Null(rig.LastFailure);
    }

    // Past the limit but inside the cap: the finish is seen, and judged late.
    public sealed class ScenarioARenderFinishesLateInsideTheCap : IAsyncLifetime
    {
        readonly Rig rig = new(engineBudgetSeconds: 120);
        (string? Path, Exception? Error) outcome;

        public async Task InitializeAsync() => outcome = await rig.RenderTakingAsync(TimeSpan.FromSeconds(200));

        public Task DisposeAsync()
        {
            rig.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheRenderStillReturns() => Assert.Null(outcome.Error);

        [Fact]
        public void TheFinishIsJudgedLate() => Assert.Equal("Took 200 s, over the 120 s limit", rig.LastFailure);
    }

    // The cap is twice the limit (240 s here), then the render is cut.
    public sealed class ScenarioARenderPassesTwiceTheLimit : IAsyncLifetime
    {
        readonly Rig rig = new(engineBudgetSeconds: 120);
        (string? Path, Exception? Error) outcome;

        public async Task InitializeAsync() => outcome = await rig.RenderTakingAsync(TimeSpan.FromSeconds(241));

        public Task DisposeAsync()
        {
            rig.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheRenderIsCancelled() => Assert.IsAssignableFrom<OperationCanceledException>(outcome.Error);

        [Fact]
        public void TheTimeoutNamesTheCap() => Assert.Equal("Timed out after 240 s", rig.LastFailure);
    }

    // A short-budget engine behaves as before: the floor is 100 s.
    public sealed class ScenarioAShortBudgetKeepsTheOldCap : IAsyncLifetime
    {
        readonly Rig rig = new(engineBudgetSeconds: 30);
        (string? Path, Exception? Error) outcome;

        public async Task InitializeAsync() => outcome = await rig.RenderTakingAsync(TimeSpan.FromSeconds(101));

        public Task DisposeAsync()
        {
            rig.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheRenderIsCancelled() => Assert.IsAssignableFrom<OperationCanceledException>(outcome.Error);

        [Fact]
        public void TheTimeoutNamesTheOldCap() => Assert.Equal("Timed out after 100 s", rig.LastFailure);
    }

    // Live: a raised budget lets the next render run past what the old budget would have allowed (240 s).
    public sealed class ScenarioTheBudgetIsRaisedLive : IAsyncLifetime
    {
        readonly Rig rig = new(engineBudgetSeconds: 120);
        (string? Path, Exception? Error) outcome;

        public async Task InitializeAsync()
        {
            rig.SetBudgetOverride(200);
            outcome = await rig.RenderTakingAsync(TimeSpan.FromSeconds(300));
        }

        public Task DisposeAsync()
        {
            rig.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheNextRenderCompletes() => Assert.Null(outcome.Error);
    }

    // Live: a lowered budget shortens the next render's cap to the 100 s floor.
    public sealed class ScenarioTheBudgetIsLoweredLive : IAsyncLifetime
    {
        readonly Rig rig = new(engineBudgetSeconds: 120);
        (string? Path, Exception? Error) outcome;

        public async Task InitializeAsync()
        {
            rig.SetBudgetOverride(30);
            outcome = await rig.RenderTakingAsync(TimeSpan.FromSeconds(101));
        }

        public Task DisposeAsync()
        {
            rig.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheNextRenderIsCut() => Assert.IsAssignableFrom<OperationCanceledException>(outcome.Error);

        [Fact]
        public void TheTimeoutNamesTheNewCap() => Assert.Equal("Timed out after 100 s", rig.LastFailure);
    }

    // A short request (ping, voices) to an engine that never answers, on a fake clock: never waits longer than 100 s,
    // whatever the engine's budget.
    sealed class HangingEngine
    {
        readonly FakeTimeProvider time = new();
        readonly TaskCompletionSource began = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Task> StartAsync(Func<SpeechEngineClient, VoiceEngine, Task> call)
        {
            var handler = new FakeHttpMessageHandler(async (_, ct) =>
            {
                began.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var client = new SpeechEngineClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new TestOptionsMonitor<TtsOptions>(new TtsOptions()),
                new EngineRenderActivity(),
                timeProvider: time);
            var engine = new VoiceEngine("chatterbox", "http://chatterbox:8004", VoiceEngineProfiles.Chatterbox, 600);

            var running = call(client, engine);
            await began.Task.WaitAsync(Guard);
            return running;
        }

        public void Advance(TimeSpan fakeTime) => time.Advance(fakeTime);

        // Real time is given for the cancel to run, so "still waiting" means the cap really has not fired.
        public async Task<bool> StillWaitingAfterAsync(Task running, TimeSpan fakeTime)
        {
            Advance(fakeTime);
            await Task.WhenAny(running, Task.Delay(TimeSpan.FromMilliseconds(200)));
            return !running.IsCompleted;
        }
    }

    public sealed class ScenarioAPingHangs
    {
        [Fact]
        public async Task ItIsStillWaitingAt99Seconds()
        {
            var engine = new HangingEngine();
            var ping = await engine.StartAsync((client, e) => client.PingAsync(e, default));
            Assert.True(await engine.StillWaitingAfterAsync(ping, TimeSpan.FromSeconds(99)));
        }

        [Fact]
        public async Task ItGivesUpAt100Seconds()
        {
            var engine = new HangingEngine();
            var ping = await engine.StartAsync((client, e) => client.PingAsync(e, default));
            engine.Advance(TimeSpan.FromSeconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ping);
        }
    }

    public sealed class ScenarioTheVoicesListHangs
    {
        [Fact]
        public async Task ItIsStillWaitingAt99Seconds()
        {
            var engine = new HangingEngine();
            var voices = await engine.StartAsync((client, e) => client.ListVoicesAsync(e, default));
            Assert.True(await engine.StillWaitingAfterAsync(voices, TimeSpan.FromSeconds(99)));
        }

        [Fact]
        public async Task ItGivesUpAt100Seconds()
        {
            var engine = new HangingEngine();
            var voices = await engine.StartAsync((client, e) => client.ListVoicesAsync(e, default));
            engine.Advance(TimeSpan.FromSeconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => voices);
        }
    }

    // The client itself no longer carries the 100 s default.
    public sealed class ScenarioTheClientIsRegistered
    {
        [Fact]
        public void TheHttpClientHasNoClientWideTimeout()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            using var services = new ServiceCollection().AddLogging().AddGenWaveTts(configuration).BuildServiceProvider();

            var http = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SpeechEngineClient));

            Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
        }
    }
}
