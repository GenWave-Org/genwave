namespace GenWave.Tts.Tests.Fakes;

using System.Net;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

/// <summary>
/// The real <see cref="VoiceEngineWarmUp"/> and <see cref="SpeechEngineClient"/> over a scripted engine transport and a
/// fake clock, with the production <see cref="EngineRenderActivity"/> between them (PLAN T768a). The cadence flips the
/// tile on the FIRST missed ping, so a tile that survives a timed-out ping did so because of the render evidence.
/// </summary>
public sealed class VoiceTileHarness : IDisposable
{
    public const string Host = "chatterbox";
    public const string OtherHost = "other-engine";

    public static readonly DependencyProbeCadence Cadence =
        new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), UnhealthyThreshold: 1);

    public VoiceEngine Engine { get; } = new("chatterbox", "http://chatterbox:8004", VoiceEngineProfiles.Chatterbox, 30);

    public VoiceEngine OtherEngine { get; } = new("other", "http://other-engine:8005", VoiceEngineProfiles.Chatterbox, 30);

    public ScriptedSpeechEngineHandler Engines { get; } = new();

    public FakeTimeProvider Time { get; } = new();

    public DependencyHealthStore Store { get; } = new();

    public SwitchableActiveEngine Active { get; }

    public SpeechEngineClient Client { get; }

    public VoiceEngineWarmUp WarmUp { get; }

    public string Key => DependencyNames.VoiceEngine(Active.Name);

    readonly string cacheRoot = Path.Combine(Path.GetTempPath(), "genwave-t768a-" + Guid.NewGuid().ToString("N"));
    readonly HttpClient http;

    public VoiceTileHarness()
    {
        Active = new SwitchableActiveEngine(Engine);
        http = new HttpClient(Engines);
        var options = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot });
        var activity = new EngineRenderActivity(Time);
        Client = new SpeechEngineClient(http, options, activity, timeProvider: Time);
        WarmUp = new VoiceEngineWarmUp(
            Active,
            Client,
            new FakeStationIdentityProvider(new StationIdentity("s", "Station", "reader-1")),
            new FixedBudget(TimeSpan.FromSeconds(30)),
            new QuietAir(),
            activity,
            Store,
            NullLogger<VoiceEngineWarmUp>.Instance,
            Time);
    }

    /// <summary>Runs one warm-up cycle, which on a fresh engine renders the boot line.</summary>
    public Task CycleAsync(CancellationToken ct = default) =>
        WarmUp.RunCycleAsync(Cadence, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);

    /// <summary>An on-air style render, left running until the scripted engine finishes it. <paramref name="audition"/> makes it a "Hear it" preview.</summary>
    public Task<string> StartRenderAsync(VoiceEngine engine, CancellationToken ct = default, bool audition = false) =>
        Client.RenderOnAsync(engine, new TtsRenderContext("Back to the music.", "reader-1", Kind: null) { IsAudition = audition }, ct);

    /// <summary>One render that the engine answers with <paramref name="status"/> after being held open for a moment.</summary>
    public async Task RenderAnsweredWithAsync(HttpStatusCode status, bool audition = false)
    {
        var began = Engines.HoldNextRender(Host);
        var render = StartRenderAsync(Engine, audition: audition);
        await began.WaitAsync(TimeSpan.FromSeconds(10));
        Engines.Finish(Host, status);
        await Record.ExceptionAsync(() => render).WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>One render that ends as an HttpClient timeout while the caller's token is live.</summary>
    public async Task RenderThatHttpTimesOutAsync()
    {
        Engines.TimeOutNextRender(Host);
        await Record.ExceptionAsync(() => StartRenderAsync(Engine)).WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>One render the caller gives up on after <paramref name="after"/> by cancelling its own token.</summary>
    public async Task RenderCancelledByCallerAfterAsync(TimeSpan after)
    {
        var began = Engines.HoldNextRender(Host);
        using var cancel = new CancellationTokenSource();
        var render = StartRenderAsync(Engine, cancel.Token);
        await began.WaitAsync(TimeSpan.FromSeconds(10));
        Time.Advance(after);
        await cancel.CancelAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await Record.ExceptionAsync(() => render).WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>One render the engine finishes fine, but only after <paramref name="after"/> on the clock.</summary>
    public async Task RenderFinishingAfterAsync(TimeSpan after)
    {
        var began = Engines.HoldNextRender(Host);
        var render = StartRenderAsync(Engine);
        await began.WaitAsync(TimeSpan.FromSeconds(10));
        Time.Advance(after);
        Engines.Finish(Host, HttpStatusCode.OK);
        await render.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        http.Dispose();
        if (Directory.Exists(cacheRoot))
            Directory.Delete(cacheRoot, recursive: true);
    }

    sealed class FixedBudget(TimeSpan budget) : IRenderBudgetProvider
    {
        public TimeSpan Current => budget;
    }

    sealed class QuietAir : IOnAirRenderSignal
    {
        public bool InFlight => false;
    }
}
