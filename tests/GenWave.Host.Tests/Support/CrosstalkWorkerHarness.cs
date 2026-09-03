// Review finding (T328 round-2, advisory e): Story328_CrosstalkStockWorker.cs and
// Story354_GapAwareStock.cs each carried their own verbatim ~70-line "build a REAL
// CrosstalkStockWorker" construction (a controllable HTTP handler standing in for the LLM backend,
// a TaskCompletionSource-blocking ITtsSynthesizer standing in for kokoro) — a `file`-scoped method
// genuinely cannot cross files, but a normal internal type in the test project's own Support/ folder
// can (mirrors GenWave.Architecture.Tests/Support's own precedent one project over). This is that
// shared home; both spec files call CrosstalkWorkerHarness.BuildAsync instead of keeping their own
// copy.

using System.Net;
using System.Text.Json;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host;
using GenWave.Host.Crosstalk;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Fakes;
using GenWave.Orchestration;
using GenWave.Tts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

// See Story328_CrosstalkStockWorker.cs's own identical comment: this test project also references
// GenWave.Loudness, which shadows the unqualified `Loudness` domain type name.
using CoreLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Support;

/// <summary><see cref="ITtsSynthesizer"/> double that blocks forever on every call until its own
/// <see cref="CancellationToken"/> fires — the "generation path" fake both real-worker spec files
/// need, standing in for kokoro at the innermost seam <see cref="CrosstalkAssembler.AssembleAsync"/>
/// actually calls (<c>RenderLinesAsync</c>'s first line synth), so a mid-flight break window has
/// something genuinely in flight to interrupt. <see cref="Entered"/> lets a test await "the fake is
/// now blocking" before advancing the clock, so the watchdog race is deterministic — never a sleep.
/// <see cref="Reset"/> re-arms <see cref="Entered"/> for a SECOND call within the same fact.</summary>
internal sealed class BlockingTtsSynthesizer : ITtsSynthesizer
{
    public TaskCompletionSource<bool> Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool WasCancelled { get; private set; }

    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct)
    {
        Entered.TrySetResult(true);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() =>
        {
            WasCancelled = true;
            tcs.TrySetCanceled(ct);
        });
        return tcs.Task;
    }

    /// <summary>Re-arms <see cref="Entered"/> with a fresh, not-yet-completed
    /// <see cref="TaskCompletionSource{TResult}"/> — call only once the PREVIOUS call's own generation
    /// has fully unwound (e.g. after awaiting the tick that used it), so there is no live continuation
    /// racing this reset.</summary>
    public void Reset() => Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
}

file sealed class FakePersonaCardStore : IPersonaStore
{
    public Dictionary<long, PersonaCard> Cards { get; } = [];

    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(Cards.TryGetValue(id, out var card) ? card : null);

    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) => throw new NotSupportedException();
    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) => throw new NotSupportedException();
    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) => throw new NotSupportedException();
    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) => throw new NotSupportedException();
    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) => throw new NotSupportedException();
}

file sealed class FakeCrosstalkScopeProvider(IReadOnlyList<string> enabledShows) : ICrosstalkScopeProvider
{
    public IReadOnlyList<string> EnabledShows { get; set; } = enabledShows;
    public int EveryNthAiring => 1;
}

file sealed class FixedEnvelopeSource : IStationDefaultEnvelopeSource
{
    public SegmentEnvelope Current => SegmentEnvelope.StationDefault;
}

file sealed class NeverCalledLoudnessAnalyzer : ILoudnessAnalyzer
{
    public Task<CoreLoudness> AnalyzeAsync(string path, CancellationToken ct) => throw new NotSupportedException();
}

file sealed class NeverCalledCueAnalyzer : ICueAnalyzer
{
    public Task<CuePoints?> AnalyzeAsync(string path, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>The two-voice crosstalk path (<see cref="CrosstalkAssembler.AssembleAsync"/>, this
/// harness's own subject) never touches <see cref="IAudioMixer"/> at all — only the widened cast path
/// (Story391, <c>AssembleCastAsync</c>) does — so this satisfies the constructor without ever being
/// called, the <see cref="NeverCalledLoudnessAnalyzer"/>/<see cref="NeverCalledCueAnalyzer"/> idiom
/// immediately above.</summary>
file sealed class NeverCalledAudioMixer : IAudioMixer
{
    public Task MixAsync(AudioMixRequest request, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>
/// Builds a REAL <see cref="CrosstalkStockWorker"/> — real <see cref="CrosstalkPlanner"/>/
/// <see cref="CrosstalkScriptWriter"/>/<see cref="CrosstalkAssembler"/>/<see cref="CachingScheduleResolver"/>/
/// <see cref="ScheduleResolver"/> — with only the external edges faked (PLAN T286 review F1/F2; SPEC
/// F140, PLAN T328). The one show named by <paramref name="showSlug"/>/<paramref name="showName"/>
/// seats the host block, flanked by distinct-persona previous/next blocks (30/20) so
/// <see cref="CrosstalkPlanner.TryCastAsync"/> casts successfully. <paramref name="now"/>'s own
/// <see cref="NowPlayingSnapshot"/> publishes as a comfortably mid-item, both-windows-clear baseline
/// (5 of 10 minutes elapsed) — a fact that means to test insufficient runway overrides it afterward
/// via the returned <see cref="NowPlayingService"/>.
/// </summary>
internal static class CrosstalkWorkerHarness
{
    /// <summary>
    /// Hygiene fix (round-N review — the leaked-temp-dir finding): every <see cref="BuildAsync"/> call
    /// used to hand <see cref="CrosstalkAssembler"/> a FRESH <c>Directory.CreateTempSubdirectory</c>
    /// root of its own, straight under the OS temp directory, with nothing ever deleting it — hundreds
    /// of orphaned <c>crosstalk-worker-test-*</c> directories accumulate on a box that has run this
    /// suite repeatedly (this file alone is called from three spec files, several times each), eventually
    /// exhausting tmpfs inodes and silently redding unrelated facts across the whole test run. ONE
    /// shared root for the WHOLE test process instead, created lazily on first use; each
    /// <see cref="BuildAsync"/> call gets its own uniquely-named SUBdirectory underneath it, and
    /// <see cref="AppDomain.ProcessExit"/> deletes the entire root, recursively, exactly once, when the
    /// test host process itself ends — no per-call disposal for every one of the many call sites across
    /// Story328/Story353/Story354 to thread through.
    /// </summary>
    static readonly string SharedTempRoot = CreateSharedTempRoot();

    static string CreateSharedTempRoot()
    {
        var root = Directory.CreateTempSubdirectory("crosstalk-worker-tests-").FullName;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup — a stray open handle at process teardown never fails the run.
            }
            catch (UnauthorizedAccessException)
            {
                // Same — teardown ordering is not guaranteed, so this is advisory, not load-bearing.
            }
        };
        return root;
    }

    static readonly string WellFormedReply = string.Join('\n', new[]
    {
        "HOST: Hey, welcome back to the show.",
        "NEIGHBOR: Great to drop in tonight.",
        "HOST: Always good to have you around.",
    });

    static PersonaCard MakeCard(string name) =>
        new(1, name, "", $"{name}'s soul.", [], new VoiceSpec("kokoro", "af_heart", 1.0, "en"),
            EnergyDisposition: 0, [], []);

    /// <param name="now">The tick clock AND the on-air item's own 10-minute-window anchor.</param>
    /// <param name="showSlug">Seated on the host block, and the ONLY show
    /// <see cref="ICrosstalkScopeProvider.EnabledShows"/> names.</param>
    /// <param name="showName">The show's display name — never read by these facts, but real
    /// (never a hardcoded placeholder) so a fact inspecting a log line sees genuine copy.</param>
    /// <param name="replyContent">The LLM backend's canned completion content — <see cref="WellFormedReply"/>
    /// by default; a fact pinning a genuine (post-generation) discard passes a reply
    /// <c>CrosstalkScriptParser</c> rejects instead.</param>
    /// <param name="llmEndpoint">SPEC F140 review finding F3's own pre-flight-refusal fact passes an
    /// empty string here — <see cref="CrosstalkScriptWriter.WriteExchangeAsync"/>'s own
    /// "Llm:Endpoint is not configured" short-circuit, discarding in milliseconds with NO generation
    /// ever attempted.</param>
    /// <param name="callRing">
    /// SPEC F139.1 (STORY-353, PLAN T330): a caller that wants to assert on what
    /// <see cref="CrosstalkStockWorker"/> stamps into the ring (e.g. a window-cancellation's
    /// <see cref="LlmCallCause.CanceledByWindow"/>) passes its OWN instance here to read back
    /// afterward. Defaults to a fresh, unobserved ring — every pre-T330 fact that never cared about
    /// the ring at all keeps compiling and passing unchanged.
    /// </param>
    /// <param name="causeCounters">The SPEC F139.2 sibling of <paramref name="callRing"/> — same
    /// "supply your own to observe it, otherwise get a fresh unobserved one" shape.</param>
    public static async Task<(
        CrosstalkStockWorker Worker, OnAirRenderGate Gate, FakeTimeProvider TimeProvider,
        NowPlayingService NowPlaying, FakeHttpMessageHandler LlmHandler, BlockingTtsSynthesizer Synthesizer)>
        BuildAsync(
            DateTimeOffset now, string showSlug, string showName, string? replyContent = null,
            string llmEndpoint = "http://fake-llm.local", LlmCallRing? callRing = null,
            LlmCallCauseCounters? causeCounters = null)
    {
        var timeProvider = new FakeTimeProvider(now);
        var gate = new OnAirRenderGate();
        var nowPlayingService = new NowPlayingService();
        nowPlayingService.Update(SingleStation.IdString, new NowPlayingSnapshot(
            "track:1", "Title", "Artist", GainDb: 0, StartedAt: now - TimeSpan.FromMinutes(5),
            DurationMs: (int)TimeSpan.FromMinutes(10).TotalMilliseconds, IsDrain: false));

        var previous = new ScheduleSegment(1, DayOfWeek.Monday, 0, 480, PersonaId: 30, Genres: null, EnergyMin: null, EnergyMax: null);
        var host = new ScheduleSegment(
            2, DayOfWeek.Monday, 480, 960, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null,
            Show: new ShowSummary(1, showName, null, null) { Slug = showSlug });
        var next = new ScheduleSegment(3, DayOfWeek.Monday, 960, 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null);
        var scheduleStore = new FakeScheduleStore(new ScheduleWeekSnapshot([previous, host, next]));
        var scheduleResolverCore = new ScheduleResolver(timeProvider, new FixedEnvelopeSource());
        var scheduleResolver = new CachingScheduleResolver(scheduleStore, scheduleResolverCore, new FakeScheduleSpecialStore());
        await scheduleResolver.ResolveAsync(CancellationToken.None);

        var personaStore = new FakePersonaCardStore();
        personaStore.Cards[10] = MakeCard("Host DJ");
        personaStore.Cards[20] = MakeCard("Next DJ");
        var planner = new CrosstalkPlanner(
            personaStore, new FakeCrosstalkScopeProvider([showSlug]), NullLogger<CrosstalkPlanner>.Instance);

        var wireResponse = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = replyContent ?? WellFormedReply }, finish_reason = "stop" } },
        });
        var llmHandler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(wireResponse, System.Text.Encoding.UTF8, "application/json"),
        }));
        // SPEC F139.1 (PLAN T330): ONE shared LlmOptions monitor for both the script writer and the
        // worker itself below — the worker's own CanceledByWindow ring stamp reads Model from this
        // SAME monitor, so a test never sees two different "Llm:Model" answers depending on which
        // collaborator it asks.
        var llmOptionsMonitor = new FakeOptionsMonitor<LlmOptions>(new LlmOptions
        {
            Endpoint = llmEndpoint, Model = "test-model", TimeoutSeconds = 5, MaxCopyChars = 300,
        });
        var ring = callRing ?? new LlmCallRing(new FakeOptionsMonitor<LlmOptions>(new LlmOptions()));
        var counters = causeCounters ?? new LlmCallCauseCounters(timeProvider);
        // SPEC F139.1/F139.2 (T330 review finding F2): ONE shared LlmCallRecorder for both the script
        // writer and the worker below — same reasoning as llmOptionsMonitor immediately above, one
        // seam over: a test that supplies its own ring/counters gets them fed by whichever collaborator
        // records first, never two independently-wrapped recorders racing to write the same pair.
        var recorder = new LlmCallRecorder(ring, counters);
        var degradationModeReader = new FakeDegradationModeReader();
        var scriptWriter = new CrosstalkScriptWriter(
            new SingleHandlerHttpClientFactory(llmHandler),
            llmOptionsMonitor,
            new FakeOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions()),
            recorder,
            degradationModeReader,
            NullLogger<CrosstalkScriptWriter>.Instance,
            timeProvider);

        var synthesizer = new BlockingTtsSynthesizer();
        var cacheRoot = Directory.CreateDirectory(Path.Combine(SharedTempRoot, Guid.NewGuid().ToString("N"))).FullName;
        var ttsOptions = new FakeOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, RenderBudgetSeconds = 30 });
        var assembler = new CrosstalkAssembler(
            synthesizer,
            new PronunciationRuleProvider(
                new FakeOptionsMonitor<TtsPronunciationsOptions>(new TtsPronunciationsOptions()),
                NullLogger<PronunciationRuleProvider>.Instance),
            new NeverCalledLoudnessAnalyzer(),
            new NeverCalledCueAnalyzer(),
            new NeverCalledAudioMixer(),
            ttsOptions,
            new FakeOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions()),
            NullLogger<CrosstalkAssembler>.Instance);

        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("st-1", "GenWave", "af_heart"));
        var stationClock = new FakeStationClockProvider(now);

        var worker = new CrosstalkStockWorker(
            planner, scriptWriter, assembler, scheduleResolver, nowPlayingService,
            identityProvider, stationClock, ttsOptions, gate,
            NullLogger<CrosstalkStockWorker>.Instance, timeProvider,
            llmOptionsMonitor, recorder, degradationModeReader);

        return (worker, gate, timeProvider, nowPlayingService, llmHandler, synthesizer);
    }
}
