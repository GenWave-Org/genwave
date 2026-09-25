// STORY-479 — Live model and voice lists (gh-#778 · SPEC F205.7 amended 2026-09-24 · PLAN T579, T580)
//
// BDD specification — xUnit. Cache-level scenarios (T579) drive ProbedChoiceCache with a fake
// IChoiceProbe + FakeTimeProvider. Unit-level scenarios (T580) drive GET/PUT /api/settings through a
// directly-constructed SettingsController wired to a real
// SettingChoiceResolver/ProbedChoiceCache/LlmModelChoiceProbe/TtsVoiceChoiceProbe — the same
// in-process pattern Story124/Story138 use for this same controller — with a fake
// ILlmModelLister/ITtsVoiceLister swapped at the boundary (mirrors Story124's FakeSettingsStore
// precedent). They pin the resolver's own dispatch logic (AC4-AC6, AC11-AC15, AC20) far more cheaply
// than a host round trip, but a directly-constructed controller proves neither DI wiring nor the
// actual wire field names — that is the entry-point scenarios' job, below. ScenarioLlmDisabled is the
// one exception at this unit level: it drives the REAL OpenAiModelLister over a fake
// HttpMessageHandler, since only an HTTP-level fake can prove NO call was made.
//
// Entry-point scenarios (T580 review finding F1, "…ScenarioAnLlmEndpointWithTwoModels…ThroughTheHost"
// and siblings, further down this file) are the ones that actually prove the feature end to end: they
// drive the SAME claims through the REAL Program.cs composition root via
// WebApplicationFactory<Program> (pattern: Story167_SpectatorModeSetting.cs) — routing, DI
// (AddHttpClient<OpenAiModelLister>, IOptionsMonitor<LlmOptions> binding of Llm:Endpoint), and the
// actual wire field names (choices/choicesStale/choicesFailed), read as raw JSON rather than round-
// tripped back through SettingDto so a renamed wire field cannot pass silently. The unit-level
// scenarios above stay IN ADDITION, not replaced, for their own cheaper dispatch-logic coverage.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Theming;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Tts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ─────────────────────────────────────────────────────────

/// <summary>A scriptable <see cref="IChoiceProbe"/> — controls the scope key and the next attempt's
/// outcome (a choice list, a thrown fault, or an indefinite hang) and records how many times
/// <see cref="FetchAsync"/> actually ran, so a scenario can pin <see cref="ProbedChoiceCache"/>'s own
/// call-once / single-flight / timeout behavior against a fake clock, never the network.</summary>
sealed class FakeChoiceProbe(string name) : IChoiceProbe
{
    public string Name { get; } = name;
    public string CurrentScopeKey { get; set; } = "https://example.local";
    public IReadOnlyList<SettingChoice> NextChoices { get; set; } = [];
    public bool Fails { get; set; }
    public bool Hangs { get; set; }

    /// <summary>When set, <see cref="FetchAsync"/> waits for this gate before it can complete — lets
    /// a scenario hold an attempt open until it has proven a SECOND caller is blocked behind it
    /// (single-flight), without a real wall-clock delay.</summary>
    public TaskCompletionSource<bool>? Gate { get; set; }

    public int CallCount { get; private set; }

    public string ScopeKey() => CurrentScopeKey;

    public async Task<IReadOnlyList<SettingChoice>> FetchAsync(CancellationToken ct)
    {
        CallCount++;

        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(ct);
        }

        if (Hangs)
        {
            // Never returns on its own — only the caller's timeout/cancellation ends this.
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }

        if (Fails)
        {
            throw new HttpRequestException("simulated probe failure");
        }

        return NextChoices;
    }
}

/// <summary>Fakes <see cref="ILlmModelLister"/> — top-level (not nested in
/// <see cref="FeatureLiveModelAndVoiceLists"/>) so the T580 review finding F1 host-level
/// scenarios' <c>LiveChoiceListsWebFactory</c> below can reference it too; an unmodified nested
/// type defaults to <c>private</c> and would not be visible there.</summary>
sealed class FakeLlmModelLister(IReadOnlyList<string> modelIds) : ILlmModelLister
{
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult(modelIds);
}

/// <summary>Fakes <see cref="ITtsVoiceLister"/> — same top-level placement reason as
/// <see cref="FakeLlmModelLister"/> immediately above.</summary>
sealed class FakeTtsVoiceLister(IReadOnlyList<string> voiceIds) : ITtsVoiceLister
{
    public Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct) => Task.FromResult(voiceIds);
}

public static class FeatureLiveModelAndVoiceLists
{
    static readonly SettingChoice Llama3 = new("llama3", "llama3");
    static readonly SettingChoice Phi4 = new("phi4", "phi4");

    /// <summary>Projects what a result actually CARRIES — Fresh's own list, Stale's carried-forward
    /// <c>LastGood</c>, or <c>[]</c> for Failed — so a fact can assert the served choices in one
    /// assertion instead of only the case name (a bare <c>result is not Stale</c> also passes for a
    /// DIFFERENT Fresh list, which is not the same claim).</summary>
    static IReadOnlyList<SettingChoice> ChoicesOf(ProbedChoiceResult result) => result switch
    {
        ProbedChoiceResult.Fresh fresh => fresh.Choices,
        ProbedChoiceResult.Stale stale => stale.LastGood,
        ProbedChoiceResult.Failed => [],
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown ProbedChoiceResult case"),
    };

    // ── Entry-point fakes/helpers (T580) ───────────────────────────────────────

    // FakeSettingsStore moved to tests/GenWave.Host.Tests/Fakes/FakeSettingsStore.cs (T580 review
    // Note 2) — was duplicated verbatim here and in Story124_EndpointLiveness.cs. FakeLlmModelLister/
    // FakeTtsVoiceLister moved to top-level scope, just above (T580 review finding F1) — LiveChoiceListsWebFactory
    // needs them too, and an unmodified nested type is private.

    static IConfiguration BuildConfig(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    /// <summary>Wires a real <see cref="SettingChoiceResolver"/> over <paramref name="probes"/> and a
    /// cold <see cref="ProbedChoiceCache"/> — the real production chain GET/PUT drives, minus DI.</summary>
    static ISettingChoiceResolver BuildResolver(params IChoiceProbe[] probes) => new SettingChoiceResolver(
        new FakeIconPackStore(),
        ThemeCatalog.LoadShipped(),
        TestSettingCopy.Real(),
        probes,
        new ProbedChoiceCache(new FakeTimeProvider(), NullLogger<ProbedChoiceCache>.Instance),
        NullLogger<SettingChoiceResolver>.Instance);

    static SettingsController BuildController(
        IConfiguration config, IStationSettingsStore store, ISettingChoiceResolver resolver) =>
        new(
            config,
            store,
            new SettingValidator(config),
            NullLogger<SettingsController>.Instance,
            TestSettingCopy.Real(),
            resolver)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    public sealed class ScenarioAnLlmEndpointWithTwoModels
    {
        // Given: fake /v1/models → [llama3, phi4]; GET /api/settings

        readonly SettingDto model;

        public ScenarioAnLlmEndpointWithTwoModels()
        {
            var probe = new LlmModelChoiceProbe(
                new Lazy<ILlmModelLister>(new FakeLlmModelLister(["llama3", "phi4"])),
                new FakeOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://llm:9000" }));
            var controller = BuildController(BuildConfig([]), new FakeSettingsStore(), BuildResolver(probe));

            var result = controller.Get(CancellationToken.None).GetAwaiter().GetResult();
            var ok = Assert.IsType<OkObjectResult>(result);
            model = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).Single(i => i.Key == "Llm:Model");
        }

        /// <summary>AC1 — choices is [llama3, phi4]</summary>
        [Fact]
        public void ListsTheModels() => Assert.Equal([Llama3, Phi4], model.Choices);

        /// <summary>AC3 — Llm:Model is kind "choice"</summary>
        [Fact]
        public void ModelIsAChoice() => Assert.Equal("choice", model.Kind);
    }

    public sealed class ScenarioATtsServerWithTwoVoices
    {
        // Given: fake ITtsVoiceLister → [af_bella, am_adam]; GET /api/settings

        static readonly SettingChoice AfBella = new("af_bella", "af_bella");
        static readonly SettingChoice AmAdam = new("am_adam", "am_adam");

        readonly SettingDto voice;

        public ScenarioATtsServerWithTwoVoices()
        {
            var probe = new TtsVoiceChoiceProbe(
                new Lazy<ITtsVoiceLister>(new FakeTtsVoiceLister(["af_bella", "am_adam"])),
                new FakeOptionsMonitor<TtsOptions>(new TtsOptions { Endpoint = "http://kokoro:8880" }));
            var controller = BuildController(BuildConfig([]), new FakeSettingsStore(), BuildResolver(probe));

            var result = controller.Get(CancellationToken.None).GetAwaiter().GetResult();
            var ok = Assert.IsType<OkObjectResult>(result);
            voice = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).Single(i => i.Key == "Station:Voice");
        }

        /// <summary>AC2 — choices is [af_bella, am_adam]</summary>
        [Fact]
        public void ListsTheVoices() => Assert.Equal([AfBella, AmAdam], voice.Choices);

        /// <summary>AC3 — Station:Voice is kind "choice"</summary>
        [Fact]
        public void VoiceIsAChoice() => Assert.Equal("choice", voice.Kind);
    }

    public sealed class ScenarioTwoResolvesWithinAMinute
    {
        // Given: a succeeding fake probe, resolved twice with the fake clock advanced 30 s

        readonly FakeChoiceProbe probe;

        public ScenarioTwoResolvesWithinAMinute()
        {
            probe = new FakeChoiceProbe("llm-model") { NextChoices = [Llama3] };
            var clock = new FakeTimeProvider();
            var cache = new ProbedChoiceCache(clock, NullLogger<ProbedChoiceCache>.Instance);

            cache.GetAsync(probe, CancellationToken.None).GetAwaiter().GetResult();
            clock.Advance(TimeSpan.FromSeconds(30));
            cache.GetAsync(probe, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>AC4 — the probe was called once</summary>
        [Fact]
        public void CallsTheProbeOnce() => Assert.Equal(1, probe.CallCount);
    }

    public sealed class ScenarioConcurrentResolvesDuringAColdCache
    {
        // Given: F205.7b — a succeeding fake probe gated behind a TaskCompletionSource, so the
        // SECOND caller is certain to be waiting on the per-probe semaphore before the first
        // attempt is allowed to complete (mirrors Story234's ScenarioIndexSingleFlight); two
        // concurrent GetAsync calls against a cold cache

        readonly FakeChoiceProbe probe;

        public ScenarioConcurrentResolvesDuringAColdCache()
        {
            var gate = new TaskCompletionSource<bool>();
            probe = new FakeChoiceProbe("llm-model") { NextChoices = [Llama3], Gate = gate };
            var cache = new ProbedChoiceCache(new FakeTimeProvider(), NullLogger<ProbedChoiceCache>.Instance);

            var first = cache.GetAsync(probe, CancellationToken.None);
            var second = cache.GetAsync(probe, CancellationToken.None);
            gate.SetResult(true);

            Task.WhenAll(first, second).GetAwaiter().GetResult();
        }

        /// <summary>F205.7b — concurrent callers share one in-flight call</summary>
        [Fact]
        public void CallsTheProbeOnce() => Assert.Equal(1, probe.CallCount);
    }

    public sealed class ScenarioAProbeThatDiesAfterOneSuccess
    {
        // Given: success [llama3, phi4], clock +61 s, failure, same ScopeKey

        static readonly IReadOnlyList<SettingChoice> Models = [Llama3, Phi4];

        readonly ProbedChoiceResult result;

        public ScenarioAProbeThatDiesAfterOneSuccess()
        {
            var probe = new FakeChoiceProbe("llm-model") { NextChoices = Models };
            var clock = new FakeTimeProvider();
            var cache = new ProbedChoiceCache(clock, NullLogger<ProbedChoiceCache>.Instance);

            cache.GetAsync(probe, CancellationToken.None).GetAwaiter().GetResult();
            clock.Advance(TimeSpan.FromSeconds(61));
            probe.Fails = true;
            result = cache.GetAsync(probe, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>AC5 — the last list</summary>
        [Fact]
        public void ServesTheLastKnownList() =>
            Assert.Equal(Models, Assert.IsType<ProbedChoiceResult.Stale>(result).LastGood);

        /// <summary>AC6 — choicesStale = true</summary>
        [Fact]
        public void FlagsItStale() => Assert.IsType<ProbedChoiceResult.Stale>(result);
    }

    public sealed class ScenarioASavedModelMissingFromTheList
    {
        // Given: Llm:Model = "mistral"; fake /v1/models → [llama3, phi4]; GET /api/settings

        readonly IReadOnlyList<SettingChoice> choices;

        public ScenarioASavedModelMissingFromTheList()
        {
            var probe = new LlmModelChoiceProbe(
                new Lazy<ILlmModelLister>(new FakeLlmModelLister(["llama3", "phi4"])),
                new FakeOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://llm:9000" }));
            var config = BuildConfig([new("Llm:Model", "mistral")]);
            var controller = BuildController(config, new FakeSettingsStore(), BuildResolver(probe));

            var result = controller.Get(CancellationToken.None).GetAwaiter().GetResult();
            var ok = Assert.IsType<OkObjectResult>(result);
            var model = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).Single(i => i.Key == "Llm:Model");
            choices = model.Choices ?? [];
        }

        /// <summary>AC7 — the last choice is ("mistral", "mistral (not found)")</summary>
        [Fact]
        public void AppendsTheSavedValue() => Assert.Equal(new SettingChoice("mistral", "mistral (not found)"), choices[^1]);
    }

    public sealed class ScenarioSavingAModelNotInTheList
    {
        // Given: fake /v1/models → [llama3, phi4]; PUT Llm:Model = "mistral"

        readonly IActionResult result;

        public ScenarioSavingAModelNotInTheList()
        {
            var probe = new LlmModelChoiceProbe(
                new Lazy<ILlmModelLister>(new FakeLlmModelLister(["llama3", "phi4"])),
                new FakeOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://llm:9000" }));
            var controller = BuildController(BuildConfig([]), new FakeSettingsStore(), BuildResolver(probe));

            result = controller.Put(
                [new SettingUpdateRequest("Llm:Model", "mistral")], CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>AC8 — 200</summary>
        [Fact]
        public void AcceptsTheSave() => Assert.IsType<OkObjectResult>(result);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAProbeThatNeverSucceeded
    {
        // Given: a failing fake probe, no prior success

        readonly ProbedChoiceResult result;

        public ScenarioAProbeThatNeverSucceeded()
        {
            var probe = new FakeChoiceProbe("llm-model") { Fails = true };
            var cache = new ProbedChoiceCache(new FakeTimeProvider(), NullLogger<ProbedChoiceCache>.Instance);

            result = cache.GetAsync(probe, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>AC11 — choices []</summary>
        [Fact]
        public void ServesNoChoices() => Assert.Empty(ChoicesOf(result));

        /// <summary>AC12 — choicesFailed = true</summary>
        [Fact]
        public void FlagsTheFailure() => Assert.IsType<ProbedChoiceResult.Failed>(result);
    }

    public sealed class ScenarioAFailureResolvedTwiceWithinAMinute
    {
        // Given: a failing fake probe, resolved twice with the clock advanced 30 s

        readonly FakeChoiceProbe probe;

        public ScenarioAFailureResolvedTwiceWithinAMinute()
        {
            probe = new FakeChoiceProbe("llm-model") { Fails = true };
            var clock = new FakeTimeProvider();
            var cache = new ProbedChoiceCache(clock, NullLogger<ProbedChoiceCache>.Instance);

            cache.GetAsync(probe, CancellationToken.None).GetAwaiter().GetResult();
            clock.Advance(TimeSpan.FromSeconds(30));
            cache.GetAsync(probe, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>AC13 — the probe was called once</summary>
        [Fact]
        public void CachesTheFailure() => Assert.Equal(1, probe.CallCount);
    }

    public sealed class ScenarioAHungProbe
    {
        // Given: a fake probe that awaits its token forever; the fake clock advanced 2 s after the call starts

        readonly ProbedChoiceResult result;

        public ScenarioAHungProbe()
        {
            var probe = new FakeChoiceProbe("llm-model") { Hangs = true };
            var clock = new FakeTimeProvider();
            var cache = new ProbedChoiceCache(clock, NullLogger<ProbedChoiceCache>.Instance);

            // Started but not awaited: advancing the fake clock fires the cache's own linked 2 s
            // timeout, which cancels the probe's indefinite Task.Delay — GetResult() then blocks
            // only for that continuation to run, never for a real 2 s wall-clock wait.
            var task = cache.GetAsync(probe, CancellationToken.None);
            clock.Advance(TimeSpan.FromSeconds(2));
            result = task.GetAwaiter().GetResult();
        }

        /// <summary>AC14 — choicesFailed after the 2 s timeout, no wall-clock wait</summary>
        [Fact]
        public void TimesOutAtTwoSeconds() => Assert.IsType<ProbedChoiceResult.Failed>(result);
    }

    public sealed class ScenarioAHungProbeNotYetTimedOutAt1Point9Seconds
    {
        // Given: a fake probe that awaits its token forever; the fake clock advanced only 1.9 s —
        // proves the timeout fires AT the 2 s bound, not merely eventually (carried from T579 review)

        readonly Task<ProbedChoiceResult> task;

        public ScenarioAHungProbeNotYetTimedOutAt1Point9Seconds()
        {
            var probe = new FakeChoiceProbe("llm-model") { Hangs = true };
            var clock = new FakeTimeProvider();
            var cache = new ProbedChoiceCache(clock, NullLogger<ProbedChoiceCache>.Instance);

            task = cache.GetAsync(probe, CancellationToken.None);
            clock.Advance(TimeSpan.FromSeconds(1.9));
        }

        /// <summary>AC14 — not yet timed out at 1.9 s</summary>
        [Fact]
        public void HasNotTimedOutYet() => Assert.False(task.IsCompleted);
    }

    public sealed class ScenarioTheEndpointChangedToAFailingOne
    {
        // Given: success on ScopeKey "A", clock +61 s, ScopeKey now "B" and failing

        readonly ProbedChoiceResult result;

        public ScenarioTheEndpointChangedToAFailingOne()
        {
            var probe = new FakeChoiceProbe("llm-model") { CurrentScopeKey = "https://a.local", NextChoices = [Llama3] };
            var clock = new FakeTimeProvider();
            var cache = new ProbedChoiceCache(clock, NullLogger<ProbedChoiceCache>.Instance);

            cache.GetAsync(probe, CancellationToken.None).GetAwaiter().GetResult();
            clock.Advance(TimeSpan.FromSeconds(61));
            probe.CurrentScopeKey = "https://b.local";
            probe.Fails = true;
            result = cache.GetAsync(probe, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>AC15 — choicesFailed = true</summary>
        [Fact]
        public void FlagsTheFailure() => Assert.IsType<ProbedChoiceResult.Failed>(result);

        /// <summary>AC15 — choicesStale = false (A's list is not served)</summary>
        [Fact]
        public void DoesNotServeTheOldList() => Assert.Empty(ChoicesOf(result));
    }

    public sealed class ScenarioLlmDisabled
    {
        // Given: Llm:Endpoint = ""; a counting fake HttpMessageHandler; GET /api/settings

        readonly FakeHttpMessageHandler handler;
        readonly SettingDto model;

        public ScenarioLlmDisabled()
        {
            handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            var optionsMonitor = new FakeOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "" });
            var probe = new LlmModelChoiceProbe(
                new Lazy<ILlmModelLister>(new OpenAiModelLister(new HttpClient(handler), optionsMonitor)), optionsMonitor);
            var controller = BuildController(
                BuildConfig([new("Llm:Endpoint", "")]), new FakeSettingsStore(), BuildResolver(probe));

            var result = controller.Get(CancellationToken.None).GetAwaiter().GetResult();
            var ok = Assert.IsType<OkObjectResult>(result);
            model = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).Single(i => i.Key == "Llm:Model");
        }

        /// <summary>AC16 — choicesFailed = true</summary>
        [Fact]
        public void FlagsTheFailure() => Assert.True(model.ChoicesFailed);

        /// <summary>AC16 — no HTTP call was made</summary>
        [Fact]
        public void MakesNoCall() => Assert.Empty(handler.Requests);
    }

    public sealed class ScenarioAMisnamedChoiceSource
    {
        // Given: an allowlist override carrying Probe("nope"); the host composed

        readonly InvalidOperationException exception;

        public ScenarioAMisnamedChoiceSource()
        {
            var allowlist = new List<AllowedSetting>
            {
                new("Llm:Model", SettingApplyMode.Live, SettingKind.Choice, "", SettingGroup.Voice, [])
                {
                    ChoiceSource = new SettingChoiceSource.Probe("nope"),
                },
            };

            exception = Assert.Throws<InvalidOperationException>(() =>
                ChoiceSourceBootCheck.Verify(allowlist, knownProbeNames: [], knownCatalogNames: []));
        }

        /// <summary>AC20 — startup throws naming "nope"</summary>
        [Fact]
        public void FailsBoot() => Assert.Contains("nope", exception.Message);
    }

    // ── Entry-point scenarios — WebApplicationFactory<Program> (T580 review finding F1) ───────
    //
    // Everything above drives SettingsController directly. The scenarios below drive the SAME
    // claims through the REAL Program.cs composition root instead — routing, DI
    // (AddHttpClient<OpenAiModelLister>, IOptionsMonitor<LlmOptions> binding of Llm:Endpoint), and
    // the actual wire field names (choices/choicesStale/choicesFailed) a directly-constructed
    // controller cannot prove. Every GET-driving scenario below reads the response body as raw JSON
    // (JsonDocument), never round-tripped back through SettingDto (T580 review finding R1) — a
    // case-insensitive round trip through the server's own record would still pass a renamed wire
    // field, defeating the point of proving the wire contract here. See LiveChoiceListsWebFactory's
    // own remarks (bottom of file) for why every GET-driving scenario fakes BOTH network edges
    // regardless of which one it is actually about.

    public sealed class ScenarioAnLlmEndpointWithTwoModelsThroughTheHost : IDisposable
    {
        // Given: the real host; fake GET {Llm:Endpoint}/v1/models -> [llama3, phi4]; GET /api/settings

        readonly LiveChoiceListsWebFactory factory;
        readonly JsonElement model;

        public ScenarioAnLlmEndpointWithTwoModelsThroughTheHost()
        {
            factory = new LiveChoiceListsWebFactory(
                llmEndpoint: "http://fake-llm.local",
                respondToLlmModelsRequest: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { data = new[] { "llama3", "phi4" } }),
                }),
                ttsVoiceIds: []);
            var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
            var body = JsonDocument.Parse(client.GetStringAsync("/api/settings").GetAwaiter().GetResult()).RootElement;
            model = body.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "Llm:Model");
        }

        public void Dispose() => factory.Dispose();

        /// <summary>AC1 — through the real host: choices is [llama3, phi4]</summary>
        [Fact]
        public void ListsTheModels() => Assert.Equal(
            ["llama3", "phi4"], model.GetProperty("choices").EnumerateArray().Select(c => c.GetProperty("value").GetString()));

        /// <summary>AC3 — through the real host: Llm:Model is kind "choice"</summary>
        [Fact]
        public void ModelIsAChoice() => Assert.Equal("choice", model.GetProperty("kind").GetString());
    }

    public sealed class ScenarioATtsServerWithTwoVoicesThroughTheHost : IDisposable
    {
        // Given: the real host; a faked ITtsVoiceLister -> [af_bella, am_adam]; GET /api/settings

        readonly LiveChoiceListsWebFactory factory;
        readonly JsonElement voice;

        public ScenarioATtsServerWithTwoVoicesThroughTheHost()
        {
            factory = new LiveChoiceListsWebFactory(
                llmEndpoint: null,
                respondToLlmModelsRequest: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
                ttsVoiceIds: ["af_bella", "am_adam"],
                // Development's own Station:Voice default ("af_heart") is not in this fake voice
                // list — left as-is, AC7's own "not found" append (shared logic, Llm:Model and
                // Station:Voice both Kind.Choice) would add a third entry this AC2 fact isn't
                // about; pin it to a value that IS on the list instead.
                stationVoice: "af_bella");
            var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
            var body = JsonDocument.Parse(client.GetStringAsync("/api/settings").GetAwaiter().GetResult()).RootElement;
            voice = body.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "Station:Voice");
        }

        public void Dispose() => factory.Dispose();

        /// <summary>AC2 — through the real host: choices is [af_bella, am_adam]</summary>
        [Fact]
        public void ListsTheVoices() => Assert.Equal(
            ["af_bella", "am_adam"], voice.GetProperty("choices").EnumerateArray().Select(c => c.GetProperty("value").GetString()));

        /// <summary>AC3 — through the real host: Station:Voice is kind "choice"</summary>
        [Fact]
        public void VoiceIsAChoice() => Assert.Equal("choice", voice.GetProperty("kind").GetString());
    }

    public sealed class ScenarioASavedModelMissingFromTheListThroughTheHost : IDisposable
    {
        // Given: the real host; Llm:Model = "mistral"; fake /v1/models -> [llama3, phi4]; GET /api/settings

        readonly LiveChoiceListsWebFactory factory;
        readonly (string? Value, string? Label) lastChoice;

        public ScenarioASavedModelMissingFromTheListThroughTheHost()
        {
            factory = new LiveChoiceListsWebFactory(
                llmEndpoint: "http://fake-llm.local",
                respondToLlmModelsRequest: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { data = new[] { "llama3", "phi4" } }),
                }),
                ttsVoiceIds: [],
                llmModel: "mistral");
            var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
            var body = JsonDocument.Parse(client.GetStringAsync("/api/settings").GetAwaiter().GetResult()).RootElement;
            var model = body.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "Llm:Model");
            var lastEntry = model.GetProperty("choices").EnumerateArray().Last();
            lastChoice = (lastEntry.GetProperty("value").GetString(), lastEntry.GetProperty("label").GetString());
        }

        public void Dispose() => factory.Dispose();

        /// <summary>AC7 — through the real host: the last choice is ("mistral", "mistral (not found)")</summary>
        [Fact]
        public void AppendsTheSavedValue() => Assert.Equal(("mistral", "mistral (not found)"), lastChoice);
    }

    public sealed class ScenarioSavingAModelNotInTheListThroughTheHost : IDisposable
    {
        // Given: the real host; fake /v1/models -> [llama3, phi4]; PUT Llm:Model = "mistral"

        readonly LiveChoiceListsWebFactory factory;
        readonly HttpStatusCode statusCode;

        public ScenarioSavingAModelNotInTheListThroughTheHost()
        {
            factory = new LiveChoiceListsWebFactory(
                llmEndpoint: "http://fake-llm.local",
                respondToLlmModelsRequest: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { data = new[] { "llama3", "phi4" } }),
                }),
                ttsVoiceIds: []);
            var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
            // F4 also means this PUT resolves ONLY Llm:Model — never touches Station:Voice's probe.
            var response = client.PutAsJsonAsync(
                "/api/settings", new[] { new SettingUpdateRequest("Llm:Model", "mistral") }).GetAwaiter().GetResult();
            statusCode = response.StatusCode;
        }

        public void Dispose() => factory.Dispose();

        /// <summary>AC8 — through the real host: PUT accepts a model not on the live list, 200</summary>
        [Fact]
        public void AcceptsTheSave() => Assert.Equal(HttpStatusCode.OK, statusCode);
    }

    public sealed class ScenarioLlmDisabledThroughTheHost : IDisposable
    {
        // Given: the real host; Llm:Endpoint explicitly blank (T580 review finding R2 — never relies
        // on the Development default happening to be unset); GET /api/settings

        readonly LiveChoiceListsWebFactory factory;
        readonly JsonElement model;

        public ScenarioLlmDisabledThroughTheHost()
        {
            factory = new LiveChoiceListsWebFactory(
                llmEndpoint: "",
                respondToLlmModelsRequest: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
                ttsVoiceIds: []);
            var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
            var body = JsonDocument.Parse(client.GetStringAsync("/api/settings").GetAwaiter().GetResult()).RootElement;
            model = body.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "Llm:Model");
        }

        public void Dispose() => factory.Dispose();

        /// <summary>AC16 — through the real host: choicesFailed = true</summary>
        [Fact]
        public void FlagsTheFailure() => Assert.True(model.GetProperty("choicesFailed").GetBoolean());

        /// <summary>AC16 — through the real host: no HTTP call reached the fake LLM transport</summary>
        [Fact]
        public void MakesNoCall() => Assert.Empty(factory.LlmHandler.Requests);
    }

    public sealed class ScenarioAMissingProbeRegistrationFailsRealBoot : IDisposable
    {
        // Given: the real host, with the tts-voices IChoiceProbe registration removed (T580 review
        // finding F3's real-boot proof, alongside ScenarioAMisnamedChoiceSource's direct-call proof
        // above)

        readonly MissingProbeWebFactory factory = new();
        readonly InvalidOperationException exception;

        public ScenarioAMissingProbeRegistrationFailsRealBoot() =>
            exception = Assert.Throws<InvalidOperationException>(() => factory.Services);

        public void Dispose() => factory.Dispose();

        /// <summary>AC20 — through the real host: the failure names the missing probe</summary>
        [Fact]
        public void NamesTheMissingProbe() =>
            Assert.Contains("tts-voices", exception.Message, StringComparison.Ordinal);
    }
}

// ── WebApplicationFactory fixtures — WebApplicationFactory<Program> (T580 review finding F1/F3) ──

/// <summary>
/// Host-level fixture for T580 review finding F1 — brings up the REAL Program.cs composition root
/// and drives GET/PUT /api/settings through it exactly the way an operator's browser does,
/// swapping only the two network edges <c>SettingChoiceResolver</c>'s live probes reach:
///
/// <list type="bullet">
///   <item><see cref="OpenAiModelLister"/>'s <see cref="HttpClient"/> transport — composes onto
///   the SAME <c>AddHttpClient&lt;OpenAiModelLister&gt;()</c> registration Program.cs's own
///   <c>TtsServiceCollectionExtensions</c> made (never replaces the whole
///   <c>IHttpClientFactory</c>, which would silently drop any <c>BaseAddress</c>/handler config the
///   real registration set — see <c>Story297_ContextTickerWire.ContextTickerFixtureWebFactory</c>'s
///   own remarks for the mutation-testing lesson behind that rule), faking only
///   <c>SendAsync</c> via <see cref="FakeHttpMessageHandler"/> so a REAL
///   <c>GET {Llm:Endpoint}/v1/models</c> request is built and routed exactly as production would,
///   just answered locally.</item>
///   <item><see cref="ITtsVoiceLister"/> — a whole-interface swap (mirrors
///   <c>Story361_AnnouncementHistoryEndpoint.AnnouncementHistoryApiWebFactory</c>'s own
///   <c>IAnnouncementStore</c> swap): the production singleton (<c>CachedVoiceLister</c> over
///   <c>KokoroVoiceLister</c>) has no typed-HttpClient seam to compose a fake transport onto the
///   way <see cref="OpenAiModelLister"/> does.</item>
/// </list>
///
/// GET /api/settings resolves EVERY <c>Kind==Choice</c> allowlisted key in parallel
/// (<c>SettingChoiceResolver.ResolveAsync</c>) — so every GET-driving scenario above fakes BOTH
/// edges regardless of which one it is actually about, even though only one edge's result is
/// asserted on; leaving the other edge real would otherwise reach <c>http://localhost:8880</c>
/// (the Development <c>Tts:Endpoint</c> default) with nothing listening. <paramref
/// name="llmEndpoint"/> left <c>null</c> keeps <c>Llm:Endpoint</c> at its Development default
/// (unset), which scenarios not asserting on the LLM edge use. The disabled-LLM shape
/// <see cref="FeatureLiveModelAndVoiceLists.ScenarioLlmDisabledThroughTheHost"/> (AC16) instead
/// passes <c>""</c> explicitly (T580 review finding R2 — the claim under test is "blank
/// <c>Llm:Endpoint</c> disables the call", so the scenario sets it itself rather than relying on the
/// Development default happening to already be blank). PUT-driving scenarios resolve only the
/// key(s) just written (T580 review finding F4), so a PUT scenario never touches the other edge
/// regardless of what this factory wires up.
///
/// <see cref="IStationSettingsStore"/> is swapped to the shared in-memory
/// <see cref="FakeSettingsStore"/> so no real Postgres connection is attempted (mirrors every
/// other SettingsController-driving factory in this suite, e.g. Story058's own
/// <c>SettingsApiWebFactory</c>).
/// </summary>
sealed class LiveChoiceListsWebFactory(
    string? llmEndpoint,
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respondToLlmModelsRequest,
    IReadOnlyList<string> ttsVoiceIds,
    string? llmModel = null,
    string? stationVoice = null)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t580-live-choice-lists";

    internal FakeHttpMessageHandler LlmHandler { get; } = new(respondToLlmModelsRequest);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        if (llmEndpoint is not null)
            builder.UseSetting("Llm:Endpoint", llmEndpoint);

        if (llmModel is not null)
            builder.UseSetting("Llm:Model", llmModel);

        if (stationVoice is not null)
            builder.UseSetting("Station:Voice", stationVoice);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IStationSettingsStore>();
            services.AddSingleton<IStationSettingsStore>(new FakeSettingsStore());

            services.AddHttpClient<OpenAiModelLister>().ConfigurePrimaryHttpMessageHandler(() => LlmHandler);

            services.RemoveAll<ITtsVoiceLister>();
            services.AddSingleton<ITtsVoiceLister>(new FakeTtsVoiceLister(ttsVoiceIds));
        });
    }

    /// <summary>Logs in via the real POST /api/auth/login round trip and returns the cookie-bearing
    /// client (Story361_AnnouncementHistoryEndpoint's own idiom).</summary>
    internal async Task<HttpClient> LoggedInClientAsync()
    {
        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"Login failed unexpectedly: {login.StatusCode}");
        return client;
    }
}

/// <summary>
/// T580 review finding F3's real-boot proof (mirrors
/// <c>Story381_ScanOptionsValidator.ScanOptionsWebFactory</c>) — removes the <c>tts-voices</c>
/// <see cref="IChoiceProbe"/> registration so <c>ChoiceSourceBootCheck.Verify</c>'s DI-derived
/// <c>knownProbeNames</c> (read straight off <c>app.Services</c> right after <c>builder.Build()</c>
/// in Program.cs) no longer names it — proving the guard fires against the REAL composition root,
/// not just <c>ChoiceSourceBootCheck.Verify</c> called directly
/// (<see cref="FeatureLiveModelAndVoiceLists.ScenarioAMisnamedChoiceSource"/>, above).
/// </summary>
sealed class MissingProbeWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t580-missing-probe";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            // Re-registers ONLY the llm probe, dropping tts-voices — the allowlist's Station:Voice
            // entry still names it (StationSettingsAllowlist.cs), so the boot check must fail.
            services.RemoveAll<IChoiceProbe>();
            services.AddSingleton<IChoiceProbe, LlmModelChoiceProbe>();
        });
    }
}
