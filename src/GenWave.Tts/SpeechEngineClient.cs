namespace GenWave.Tts;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// The one typed-HttpClient egress type for the OpenAI-style speech contract (SPEC F219.4–F219.5):
/// <c>POST {Endpoint}/v1/audio/speech</c> and <c>GET {Endpoint}/v1/audio/voices</c> for ANY
/// <see cref="VoiceEngine"/>. The engine (endpoint + profile) is a per-call argument, so adding an
/// engine is configuration, not a new client. No boot-frozen BaseAddress (F36): the absolute URI is
/// built per call. Piper's bare text/plain wire is a different contract, rendered by <see cref="RenderPiperAsync"/> against the same engine endpoint.
/// </summary>
public sealed class SpeechEngineClient(
    HttpClient http,
    IOptionsMonitor<TtsOptions> optionsMonitor,
    EngineRenderActivity activity,
    PronunciationRuleHitReporter? ruleHits = null,
    TimeProvider? timeProvider = null,
    ILogger<SpeechEngineClient>? logger = null,
    PaceStretch? paceStretch = null)
{
    // Ping and voices never wait longer than this; it is also the floor of a render's cap, so a short-budget engine is cut where it always was.
    static readonly TimeSpan ShortRequestCap = TimeSpan.FromSeconds(100);

    readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    // SPEC F219.31: the pace of an engine with no speed of its own, applied to its output right here, below every
    // measure and cache. Its warnings log under the SpeechEngineClient category (it is handed this client's logger).
    readonly PaceStretch stretch = paceStretch ?? new PaceStretch(logger ?? NullLogger<SpeechEngineClient>.Instance);

    /// <summary>
    /// Renders on one NAMED registry engine over its own wire: Piper's bare text/plain for the piper profile, the
    /// OpenAI-style speech contract for every other. The one place that picks the wire for an engine, shared by the
    /// active-engine primary (<see cref="ActiveEngineSynthesizer"/>) and the admin preview of a chosen engine.
    /// Every render here also reports to <see cref="EngineRenderActivity"/>, which the voice tile reads (SPEC F219.25):
    /// in flight while it runs, then a failure (5xx, transport, timeout, or a finish past the engine's limit), a success,
    /// or no verdict (4xx, an early caller cancel). Auditions count as in flight only. A new caller affects the tile.
    /// </summary>
    public async Task<string> RenderOnAsync(VoiceEngine engine, TtsRenderContext context, CancellationToken ct)
    {
        // T768a: tell the voice tile a render is in flight, so a busy engine that cannot answer a ping is not called down.
        var identity = engine.Identity;
        var startedAt = time.GetTimestamp();
        using var cap = RenderCapFor(engine, ct);
        activity.Started(identity);
        string path;
        try
        {
            path = await DispatchAsync(engine, context, cap.Token);
            Settle(engine, context, LateReason(engine, startedAt));
        }
        catch (Exception ex)
        {
            Settle(engine, context, FaultReason(engine, ex, startedAt, cap, ct));
            throw;
        }

        // SPEC F219.31: the local ffmpeg stretch runs AFTER the engine is judged, so its time is never charged to the
        // engine (T768a's late window) and a cancel inside it is no engine fault.
        return await StretchAsync(engine, context, path, ct);
    }

    Task<string> DispatchAsync(VoiceEngine engine, TtsRenderContext context, CancellationToken capped) =>
        engine.Profile == VoiceEngineProfiles.Piper
            ? PostPiperAsync(engine, context, capped)
            : PostSpeechAsync(engine, context, capped);

    // Whatever escapes the stretch (a caller cancel), the file the engine wrote is deleted: nothing else holds its path.
    async Task<string> StretchAsync(VoiceEngine engine, TtsRenderContext context, string path, CancellationToken ct)
    {
        try
        {
            return await stretch.ApplyAsync(engine.Profile, context.Pace, path, ct);
        }
        catch
        {
            DeleteBestEffort(path);
            throw;
        }
    }

    static void DeleteBestEffort(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Scratch file under CacheRoot with a unique name: an orphan is inert, and the original failure must surface.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }

    // fault null = no verdict on the engine. An audition ("Hear it") is never evidence either way: it still occupied the
    // engine while it ran (so it counted as in flight), but a bad preview must not turn the tile red nor keep it green.
    void Settle(VoiceEngine engine, TtsRenderContext context, RenderFault? fault)
    {
        if (context.IsAudition || fault is null)
            activity.Abandoned(engine.Identity);
        else if (fault.IsFailure)
            activity.Failed(engine.Identity, fault.Reason);
        else
            activity.Succeeded(engine.Identity);
    }

    /// <summary>
    /// How long one render request may stay open (T771a): twice the engine's live limit, never under 100 s. The cap sits past
    /// the limit on purpose: the feeder gives up at the limit and lets the render finish, and that late finish must still be
    /// seen and judged late. Read at the start of each render, so a live budget edit applies to the next one.
    /// </summary>
    RequestCap RenderCapFor(VoiceEngine engine, CancellationToken ct) =>
        new(TimeSpan.FromSeconds(Math.Max(ShortRequestCap.TotalSeconds, 2d * LimitSeconds(engine))), time, ct);

    int LimitSeconds(VoiceEngine engine) => EngineRenderBudget.For(engine, optionsMonitor.CurrentValue);

    bool PastTimeLimit(VoiceEngine engine, long startedAt) =>
        time.GetElapsedTime(startedAt) >= TimeSpan.FromSeconds(LimitSeconds(engine));

    /// <summary>
    /// A render that came back is a success, unless it took longer than the engine's limit: the feeder never cancels at
    /// the budget (it gives up on the line and lets the render finish), so a late finish is a timeout, not health.
    /// </summary>
    RenderFault LateReason(VoiceEngine engine, long startedAt) =>
        PastTimeLimit(engine, startedAt)
            ? RenderFault.Failure($"Took {time.GetElapsedTime(startedAt).TotalSeconds:F0} s, over the {LimitSeconds(engine)} s limit")
            : RenderFault.Success;

    /// <summary>
    /// What a thrown render says about the engine. Only 5xx, transport errors and timeouts are engine failures; a 4xx is a
    /// bad line or request, and a caller cancel inside the limit is the caller walking away: both are no verdict (null).
    /// </summary>
    RenderFault? FaultReason(VoiceEngine engine, Exception ex, long startedAt, RequestCap cap, CancellationToken ct) => ex switch
    {
        // The cap passing is a timeout whatever else fired with it. Otherwise the caller's token fired: only past the
        // engine's limit is that a timeout. A live caller token means something other than the caller ended the request.
        OperationCanceledException when cap.TimedOut => RenderFault.Failure($"Timed out after {cap.After.TotalSeconds:F0} s"),
        OperationCanceledException when ct.IsCancellationRequested =>
            PastTimeLimit(engine, startedAt) ? RenderFault.Failure($"Timed out after {LimitSeconds(engine)} s") : null,
        OperationCanceledException => RenderFault.Failure($"Timed out after {cap.After.TotalSeconds:F0} s"),
        HttpRequestException { StatusCode: null } => RenderFault.Failure(ex.Message),
        HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } => RenderFault.Failure(ex.Message),
        _ => null,
    };

    public async Task<string> RenderAsync(VoiceEngine engine, TtsRenderContext context, CancellationToken ct)
    {
        using var cap = RenderCapFor(engine, ct);
        return await PostSpeechAsync(engine, context, cap.Token);
    }

    async Task<string> PostSpeechAsync(VoiceEngine engine, TtsRenderContext context, CancellationToken ct)
    {
        var cfg = optionsMonitor.CurrentValue;
        // SPEC F219.17: the engine that actually renders (a fallback may differ from the one the text was gated for) only ever
        // receives tags its own profile lists; an engine with none receives no bracket tag at all.
        var text = SoundTags.Apply(context.Text, engine.Profile, int.MaxValue);
        var body = SpeechRequestBody.Build(
            engine.Profile, text, context.Voice, cfg.Format,
            PronunciationRuleSet.FromContext(context.Rules), cfg.SentencePauseSeconds, context.Pace,
            out var matches);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await http.PostAsync(EndpointUri.Combine(engine.Endpoint, "/v1/audio/speech"), content, ct);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var path = await TransientAudioWriter.WriteAsync(cfg, bytes, ct);

        // Only after the audio landed: "fired" means "aired" (STORY-253, T142).
        ruleHits?.Report(matches, context.Kind, context.IsAudition);
        return path;
    }

    /// <summary>Renders over Piper's bare text/plain wire at the engine's own endpoint (SPEC F99.4, F219.1).</summary>
    public async Task<string> RenderPiperAsync(VoiceEngine engine, TtsRenderContext context, CancellationToken ct)
    {
        using var cap = RenderCapFor(engine, ct);
        return await PostPiperAsync(engine, context, cap.Token);
    }

    Task<string> PostPiperAsync(VoiceEngine engine, TtsRenderContext context, CancellationToken ct) =>
        PiperWireProtocol.RenderAsync(http, engine.Endpoint, context, optionsMonitor.CurrentValue, ct);

    /// <summary>
    /// Reachability only: <c>OPTIONS {endpoint}/</c>. Any HTTP response at all proves the engine process is up and
    /// accepting connections (the status is not the signal, as with <see cref="PiperHealthProbe"/>); a refused
    /// connection or a timeout throws. Works for every profile because it names no engine-specific route.
    /// </summary>
    public async Task PingAsync(VoiceEngine engine, CancellationToken ct)
    {
        using var cap = new RequestCap(ShortRequestCap, time, ct);
        using var request = new HttpRequestMessage(HttpMethod.Options, EndpointUri.Combine(engine.Endpoint, "/"));
        using var response = await http.SendAsync(request, cap.Token);
    }

    public async Task<IReadOnlyList<string>> ListVoicesAsync(VoiceEngine engine, CancellationToken ct)
    {
        using var cap = new RequestCap(ShortRequestCap, time, ct);
        var response = await http.GetAsync(EndpointUri.Combine(engine.Endpoint, "/v1/audio/voices"), cap.Token);
        response.EnsureSuccessStatusCode();

        // Kokoro and the bench shims both answer { "voices": [...] }; the parser takes either entry shape.
        var payload = await response.Content.ReadFromJsonAsync<KokoroVoicesResponse>(cap.Token);
        return payload?.VoiceIds() ?? [];
    }
}
