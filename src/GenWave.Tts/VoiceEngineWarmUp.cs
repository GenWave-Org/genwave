namespace GenWave.Tts;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;

/// <summary>
/// Keeps the voice tile honest about the ACTIVE engine (SPEC F219.25, F219.27, STORY-560). Per cycle it reads the live
/// <see cref="IActiveVoiceEngine"/>, so a saved <c>Tts:Engine</c> switch is followed with no restart:
/// <list type="bullet">
/// <item>A new engine (first cycle after boot, a switch, or a repointed endpoint) gets one short real line rendered
/// through <see cref="SpeechEngineClient"/> at its own endpoint, bounded by that engine's live render budget. Green
/// only after it succeeds; red with a plain reason on refusal, error or timeout. The audio is deleted: never cached,
/// never aired. Cold engines take 19 to 48 s, which is why the first line is a real render and not a ping.</item>
/// <item>Once warmed, each cycle is a cheap reachability check (<see cref="SpeechEngineClient.PingAsync"/>), unless
/// <see cref="EngineRenderActivity"/> already says: a render in flight or one that succeeded within the interval counts as
/// reachable with no ping (a busy single-threaded engine cannot answer one), and two renders that failed or timed out in a
/// row (no success between) turn the tile red on that cycle; one alone is a grace miss, like one missed ping. A flip to unhealthy forgets the warm-up, so green again needs another real line.</item>
/// </list>
/// The warm-up never touches <see cref="FallbackTtsSynthesizer"/>: a down engine can only ever be reported, never
/// rendered by another engine. It does not start while an on-air render is in flight (<see cref="IOnAirRenderSignal"/>):
/// a cold engine on a shared CPU must not slow a break that is being built, so the tile reads "warming up" until the
/// next quiet cycle. Once started it is NOT aborted when an on-air render begins (unlike the content sweeps): cancelling
/// the request does not stop the engine's own render, so nothing would be freed, and the cold cost is paid only once. Verdicts are written under <see cref="DependencyNames.VoiceEngine"/>, read by
/// <see cref="VoiceHealthReader"/>. Loop shape mirrors <see cref="DependencyHealthProber"/>; the Host shell is a thin
/// BackgroundService, so startup and playout never wait on this.
/// </summary>
public sealed class VoiceEngineWarmUp(
    IActiveVoiceEngine active,
    SpeechEngineClient client,
    IStationIdentityProvider station,
    IRenderBudgetProvider budget,
    IOnAirRenderSignal onAir,
    EngineRenderActivity activity,
    DependencyHealthStore store,
    ILogger<VoiceEngineWarmUp> logger,
    TimeProvider? timeProvider = null)
{
    /// <summary>The boot line: short, so a CPU engine renders it quickly once warm.</summary>
    public const string WarmUpText = "Testing, one, two.";

    readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    // Identity of the engine instance last seen / last warmed: name plus endpoint, so a repoint re-warms.
    string? observedEngine;
    string? observedKey;
    string? warmedEngine;

    public async Task RunAsync(Func<DependencyProbeCadence> cadence, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(cadence().Interval, time);
        do
        {
            await RunCycleAsync(cadence(), ct);
            timer.Period = cadence().Interval;
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    /// <summary>One pass; never throws except the caller's own cancellation.</summary>
    public async Task RunCycleAsync(DependencyProbeCadence cadence, CancellationToken ct)
    {
        if (active.Current is not { } engine)
        {
            Forget(ref observedKey);
            observedEngine = null;
            warmedEngine = null;
            return;   // the reader reports "not configured"
        }

        var identity = engine.Identity;
        var key = DependencyNames.VoiceEngine(engine.Name);
        if (identity != observedEngine)
        {
            // A different engine: the previous engine's verdict is no longer ours to publish (A, then B, then A must not
            // show A's old green), and nothing under the new key is evidence about it yet.
            Forget(ref observedKey);
            store.Forget(key);
            observedKey = key;
            observedEngine = identity;
            warmedEngine = null;
        }

        if (warmedEngine == identity)
        {
            await CheckReachableAsync(engine, key, identity, cadence, ct);
            return;
        }

        if (onAir.InFlight)
            return;   // yield: try again next cycle

        await WarmUpAsync(engine, key, identity, ct);
    }

    async Task WarmUpAsync(VoiceEngine engine, string key, string identity, CancellationToken ct)
    {
        var voice = station.Current;
        if (voice.VoiceMissing)
        {
            Fail(key, engine, $"The station has no voice set for {engine.Name}.", ex: null);
            return;
        }

        var limit = budget.Current;
        using var timeout = new CancellationTokenSource(limit, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            var path = await client.RenderOnAsync(engine, new TtsRenderContext(WarmUpText, voice.Voice, Kind: null), linked.Token);
            DeleteQuietly(path);
            warmedEngine = identity;
            if (store.GetVerdict(key) is { Healthy: false })
                logger.LogInformation("Voice engine {Engine} rendered its warm-up line; the voice tile is green again", engine.Name);
            store.Record(key, healthy: true, reason: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            Fail(key, engine, $"Timed out after {limit.TotalSeconds:F0} s", ex: null);
        }
        catch (Exception ex)
        {
            Fail(key, engine, ex.Message, ex);
        }
    }

    async Task CheckReachableAsync(VoiceEngine engine, string key, string identity, DependencyProbeCadence cadence, CancellationToken ct)
    {
        // T768a: real speech renders are better evidence than a ping, which a busy single-threaded engine cannot answer.
        var evidence = activity.Read(identity);
        if (evidence.IsDown)
        {
            // Applied on this cycle, not at the moment of the second failure: this loop is the only writer of the verdict
            // and of warmedEngine. One failed render alone is a grace miss and falls through to the checks below.
            activity.ClearFailure(identity);
            Fail(key, engine, evidence.Failure ?? "Speech renders are failing", ex: null);
            warmedEngine = null;
            return;
        }

        if (evidence.InFlight || evidence.SucceededWithin(time.GetUtcNow(), cadence.Interval))
        {
            store.Record(key, healthy: true, reason: null);
            return;
        }

        using var timeout = new CancellationTokenSource(cadence.PerProbeTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await client.PingAsync(engine, linked.Token);
            store.Record(key, healthy: true, reason: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            RecordUnreachable(engine, key, identity, cadence, $"Timed out after {cadence.PerProbeTimeout.TotalSeconds:F0} s", ex: null);
        }
        catch (Exception ex)
        {
            RecordUnreachable(engine, key, identity, cadence, ex.Message, ex);
        }
    }

    void RecordUnreachable(VoiceEngine engine, string key, string identity, DependencyProbeCadence cadence, string reason, Exception? ex)
    {
        // Debounced like every other probe: one missed ping is not an outage. The flip forgets the warm-up.
        var verdict = store.Record(key, healthy: false, reason, cadence.UnhealthyThreshold);
        if (verdict.Healthy)
        {
            logger.LogDebug(ex, "Voice engine {Engine} reachability check failed ({Reason}); verdict still healthy", engine.Name, reason);
            return;
        }

        if (warmedEngine == identity)
            logger.LogWarning(ex, "Voice engine {Engine} is unreachable ({Reason}); the voice tile is red", engine.Name, reason);
        warmedEngine = null;
    }

    void Fail(string key, VoiceEngine engine, string reason, Exception? ex)
    {
        // Not debounced: no verdict, or a first failure, must never read green (threshold 1).
        var wasFailing = store.GetVerdict(key) is { Healthy: false };
        store.Record(key, healthy: false, reason);
        if (!wasFailing)
            logger.LogWarning(ex, "Voice engine {Engine} failed ({Reason}); the voice tile is red", engine.Name, reason);
        else
            logger.LogDebug(ex, "Voice engine {Engine} still failing ({Reason})", engine.Name, reason);
    }

    void Forget(ref string? key)
    {
        if (key is not null)
            store.Forget(key);
        key = null;
    }

    void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The line rendered; a leftover transient file must never turn that success into a red tile.
            logger.LogDebug(ex, "Could not delete the warm-up audio {Path}", path);
        }
    }
}
