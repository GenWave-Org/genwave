// STORY-479 — Live model and voice lists (gh-#778 · SPEC F205.7 amended 2026-09-24 · PLAN T579, T580)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending…)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.
// Cache-level scenarios (T579) drive ProbedChoiceCache with a fake IChoiceProbe + FakeTimeProvider; entry-point
// scenarios (T580) drive GET/PUT /api/settings through WebApplicationFactory with a fake HttpMessageHandler.

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using GenWave.Host.Configuration;

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

public static class FeatureLiveModelAndVoiceLists
{
    const string PendingApi = "pending: T580 — probes + resolver + DTO (STORY-479)";

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

    public sealed class ScenarioAnLlmEndpointWithTwoModels
    {
        // Given: fake /v1/models → [llama3, phi4]; GET /api/settings

        /// <summary>AC1 — choices is [llama3, phi4]</summary>
        [Fact(Skip = PendingApi)]
        public void ListsTheModels() => Assert.Fail(PendingApi);

        /// <summary>AC3 — Llm:Model is kind "choice"</summary>
        [Fact(Skip = PendingApi)]
        public void ModelIsAChoice() => Assert.Fail(PendingApi);
    }

    public sealed class ScenarioATtsServerWithTwoVoices
    {
        // Given: fake ITtsVoiceLister → [af_bella, am_adam]; GET /api/settings

        /// <summary>AC2 — choices is [af_bella, am_adam]</summary>
        [Fact(Skip = PendingApi)]
        public void ListsTheVoices() => Assert.Fail(PendingApi);

        /// <summary>AC3 — Station:Voice is kind "choice"</summary>
        [Fact(Skip = PendingApi)]
        public void VoiceIsAChoice() => Assert.Fail(PendingApi);
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

        /// <summary>AC7 — the last choice is ("mistral", "mistral (not found)")</summary>
        [Fact(Skip = PendingApi)]
        public void AppendsTheSavedValue() => Assert.Fail(PendingApi);
    }

    public sealed class ScenarioSavingAModelNotInTheList
    {
        // Given: fake /v1/models → [llama3, phi4]; PUT Llm:Model = "mistral"

        /// <summary>AC8 — 200</summary>
        [Fact(Skip = PendingApi)]
        public void AcceptsTheSave() => Assert.Fail(PendingApi);
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

        /// <summary>AC16 — choicesFailed = true</summary>
        [Fact(Skip = PendingApi)]
        public void FlagsTheFailure() => Assert.Fail(PendingApi);

        /// <summary>AC16 — no HTTP call was made</summary>
        [Fact(Skip = PendingApi)]
        public void MakesNoCall() => Assert.Fail(PendingApi);
    }

    public sealed class ScenarioAMisnamedChoiceSource
    {
        // Given: an allowlist override carrying Probe("nope"); the host composed

        /// <summary>AC20 — startup throws naming "nope"</summary>
        [Fact(Skip = PendingApi)]
        public void FailsBoot() => Assert.Fail(PendingApi);
    }
}
