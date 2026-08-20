// GH-486 — settings: whole-array writes have no version guard — lost updates on concurrent edits
//
// BDD specification — xUnit. In-process: constructs SettingsController/PronunciationsController
// directly against a scriptable IStationSettingsStore fake carrying REAL optimistic-concurrency
// semantics (in-memory key -> (value, version), matching StationSettingsRepository's own
// WriteIfVersionMatchesAsync contract exactly) — the real SQL itself is proven separately, against
// a real Postgres, in GenWave.MediaLibrary.Tests/Specs/Story042_StationSettingsRepository.cs
// (ScenarioWriteIfVersionMatchesHappyPath/Conflict, ScenarioReadVersions).
//
// A genuine two-request race is reproduced deterministically (no real threading needed) via
// VersionedFakeSettingsStore.BumpKeyAfterNextRead: the controller's OWN read sees version N; the
// store then silently advances to N+1 — standing in for a DIFFERENT request's write landing in the
// gap — before the controller's own write (still carrying N) is attempted. This is the exact
// "DELETE || PUT both 2xx, the edit silently vanished" probe the issue was filed from.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Fakes;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSettingsVersionGuard
{
    // ── In-process fakes ────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="IStationSettingsStore"/> test double carrying REAL optimistic-concurrency
    /// semantics (0 = no row yet; otherwise a write only lands if the stored version still
    /// matches) — mirrors <c>StationSettingsRepository.WriteIfVersionMatchesAsync</c>'s own
    /// contract in memory.
    /// </summary>
    sealed class VersionedFakeSettingsStore : IStationSettingsStore
    {
        readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, long> versions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Set before a call the scenario wants to race: the NEXT <see cref="ReadVersionsAsync"/>
        /// call returns THIS key's version as of right now, then immediately advances it by one —
        /// a different request's write landing between the controller's own read and its own
        /// subsequent write. Works even for a key with no prior row (advances 0 -> 1), the same
        /// "no row yet" case <see cref="WriteIfVersionMatchesAsync"/>'s expectedVersion=0 branch
        /// covers.
        /// </summary>
        public string? BumpKeyAfterNextRead { get; set; }

        public int ConditionalWriteAttempts { get; private set; }

        public string? ValueOf(string key) => values.TryGetValue(key, out var v) ? v : null;

        public long VersionOf(string key) => versions.GetValueOrDefault(key, 0);

        public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
        {
            values[key] = value?.ToString() ?? string.Empty;
            versions[key] = versions.GetValueOrDefault(key, 0) + 1;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyDictionary<string, long>> ReadVersionsAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = new Dictionary<string, long>(versions, StringComparer.OrdinalIgnoreCase);
            if (BumpKeyAfterNextRead is { } key)
            {
                BumpKeyAfterNextRead = null;
                versions[key] = versions.GetValueOrDefault(key, 0) + 1;
            }
            return Task.FromResult<IReadOnlyDictionary<string, long>>(snapshot);
        }

        public Task<SettingsWriteOutcome> WriteIfVersionMatchesAsync(
            string key, object value, long expectedVersion, CancellationToken cancellationToken = default)
        {
            ConditionalWriteAttempts++;
            var current = versions.GetValueOrDefault(key, 0);
            if (current != expectedVersion)
                return Task.FromResult(SettingsWriteOutcome.Conflict);

            values[key] = value?.ToString() ?? string.Empty;
            versions[key] = current + 1;
            return Task.FromResult(SettingsWriteOutcome.Written);
        }
    }

    /// <summary>Minimal <see cref="IOptionsMonitor{T}"/> that returns <see cref="CurrentValue"/> on
    /// every read (the house idiom — mirrors Story123_PreviewEndpoints's own).</summary>
    sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    static IConfiguration BuildConfig(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    static IEnumerable<KeyValuePair<string, string?>> AllDefaults() =>
    [
        new("Loudness:TargetLufs", "-16"),
        new("Loudness:CeilingDbtp", "-1"),
        new("Tts:Corrections", "[]"),
        new("Tts:Pronunciations", "[]"),
    ];

    static SettingsController BuildSettingsController(VersionedFakeSettingsStore store, IConfiguration? config = null)
    {
        config ??= BuildConfig(AllDefaults());
        return new SettingsController(
            config,
            store,
            new SettingValidator(config),
            NullLogger<SettingsController>.Instance,
            new FakeIconPackStore())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    static PronunciationsController BuildPronunciationsController(
        VersionedFakeSettingsStore store, IReadOnlyList<PronunciationRule> declaredStationRules) =>
        new(
            new FakeOptionsMonitor<TtsPronunciationsOptions>(
                new TtsPronunciationsOptions { Pronunciations = PronunciationRuleJson.Serialize(declaredStationRules) }),
            store,
            new ActivePersonaPronunciationRulesCache(new FakeActivePersonaAccessor(), TimeProvider.System),
            new PronunciationRuleHitStats(),
            new SpeechCorrectionProvider(
                new FakeOptionsMonitor<TtsCorrectionsOptions>(new TtsCorrectionsOptions()),
                NullLogger<SpeechCorrectionProvider>.Instance),
            new ActivePersonaCorrectionsCache(new FakeActivePersonaAccessor(), TimeProvider.System),
            NullLogger<PronunciationsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    // =========================================================================
    // SettingsController — GET carries the version token
    // =========================================================================

    public sealed class ScenarioGetCarriesVersion
    {
        [Fact]
        public async Task AKeyWithNoOverrideReportsVersionZero()
        {
            var controller = BuildSettingsController(new VersionedFakeSettingsStore());

            var result = await controller.Get(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value);
            Assert.Equal(0, items.Single(i => i.Key == "Tts:Corrections").Version);
        }

        [Fact]
        public async Task AWrittenKeyReportsItsStoredVersion()
        {
            var store = new VersionedFakeSettingsStore();
            await store.WriteAsync("Tts:Corrections", "[]", CancellationToken.None);
            var controller = BuildSettingsController(store);

            var result = await controller.Get(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value);
            Assert.Equal(1, items.Single(i => i.Key == "Tts:Corrections").Version);
        }
    }

    // =========================================================================
    // SettingsController — PUT, happy path
    // =========================================================================

    public sealed class ScenarioPutWithoutExpectedVersion
    {
        [Fact]
        public async Task WritesUnconditionallyExactlyAsBeforeGh486()
        {
            // No ExpectedVersion supplied — every pre-gh-#486 caller's exact shape.
            var store = new VersionedFakeSettingsStore();
            var controller = BuildSettingsController(store);

            var result = await controller.Put(
                [new SettingUpdateRequest("Tts:Corrections", "[{\"from\":\"a\",\"to\":\"b\"}]")],
                CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal("[{\"from\":\"a\",\"to\":\"b\"}]", store.ValueOf("Tts:Corrections"));
        }
    }

    public sealed class ScenarioPutWithMatchingExpectedVersion
    {
        [Fact]
        public async Task WritesAndAdvancesTheVersion()
        {
            var store = new VersionedFakeSettingsStore();
            var controller = BuildSettingsController(store);

            var result = await controller.Put(
                [new SettingUpdateRequest("Tts:Corrections", "[]", ExpectedVersion: 0)],
                CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value);
            Assert.Equal(1, items.Single(i => i.Key == "Tts:Corrections").Version);
            Assert.Equal(1, store.VersionOf("Tts:Corrections"));
        }
    }

    // =========================================================================
    // SettingsController — PUT, the lost-update race (gh-#486)
    // =========================================================================

    public sealed class ScenarioPutLosesTheVersionRace
    {
        [Fact]
        public async Task AStaleExpectedVersionIsRejectedWith409()
        {
            // Given this editor read Tts:Corrections at version 0, but a DIFFERENT request wrote
            // it first — the store is now at version 1, unseen by this editor's own stale read ...
            var store = new VersionedFakeSettingsStore();
            await store.WriteAsync("Tts:Corrections", "[{\"from\":\"theirs\",\"to\":\"kept\"}]", CancellationToken.None);
            var controller = BuildSettingsController(store);

            // When this editor's own save reaches the server, still carrying the stale version ...
            var result = await controller.Put(
                [new SettingUpdateRequest("Tts:Corrections", "[{\"from\":\"mine\",\"to\":\"lost\"}]", ExpectedVersion: 0)],
                CancellationToken.None);

            // Then it is rejected — never silently overwrites the concurrent save.
            var conflict = Assert.IsType<ConflictObjectResult>(result);
            var problem = Assert.IsType<ProblemDetails>(conflict.Value);
            Assert.Equal(SettingsProblemTypes.VersionConflict, problem.Type);
            Assert.Equal(409, problem.Status);
            Assert.Contains("Tts:Corrections", problem.Detail);
            Assert.Equal("[{\"from\":\"theirs\",\"to\":\"kept\"}]", store.ValueOf("Tts:Corrections"));
        }

        [Fact]
        public async Task ALaterEntryInTheSameBatchIsNeverAttemptedOnceAnEarlierOneConflicts()
        {
            // Given a batch with a guarded key first (already stale — the store moved past the
            // version this editor is about to claim), then an ordinary key second ...
            var store = new VersionedFakeSettingsStore();
            await store.WriteAsync("Tts:Corrections", "[]", CancellationToken.None);
            var controller = BuildSettingsController(store);

            var result = await controller.Put(
                [
                    new SettingUpdateRequest("Tts:Corrections", "[]", ExpectedVersion: 0),
                    new SettingUpdateRequest("Loudness:TargetLufs", "-10"),
                ],
                CancellationToken.None);

            // Then the whole request 409s, and the second entry was never persisted.
            Assert.IsType<ConflictObjectResult>(result);
            Assert.Null(store.ValueOf("Loudness:TargetLufs"));
        }
    }

    // =========================================================================
    // PronunciationsController — the T144 probe reproduced (gh-#486)
    // =========================================================================

    public sealed class ScenarioPronunciationsCreateLosesTheRace
    {
        [Fact]
        public async Task A409NamesItAVersionConflictNeverADuplicateIdentityOne()
        {
            var store = new VersionedFakeSettingsStore { BumpKeyAfterNextRead = "Tts:Pronunciations" };
            var controller = BuildPronunciationsController(store, []);

            var result = await controller.Create(
                new PronunciationRuleWriteRequest("Reykjavík", null, "/ˈreɪkjaviːk/"), CancellationToken.None);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            var problem = Assert.IsType<ProblemDetails>(conflict.Value);
            Assert.Equal(SettingsProblemTypes.VersionConflict, problem.Type);
            Assert.Equal(409, problem.Status);
        }
    }

    public sealed class ScenarioPronunciationsDeleteVsUpdateRace
    {
        [Fact]
        public async Task TheConcurrentEditSurvivesAndTheLosingDeleteIs409NotSilentlyLost()
        {
            // Given a station rule, and Update lands FIRST (advancing the version this Delete
            // request read past) — the exact "DELETE || PUT both 2xx, one edit vanished" T144
            // probe the issue was filed from.
            var declared = new List<PronunciationRule> { new("MacLeod", "MacLeod", "/muhk-loud/") };
            var seedStore = new VersionedFakeSettingsStore();
            await seedStore.WriteAsync(
                "Tts:Pronunciations", PronunciationRuleJson.Serialize(declared), CancellationToken.None);

            // The Delete request's own controller instance read the rule set at the seeded
            // version; BumpKeyAfterNextRead simulates the concurrent Update committing in the gap
            // between this Delete's own read and its own write.
            seedStore.BumpKeyAfterNextRead = "Tts:Pronunciations";
            var deleteController = BuildPronunciationsController(seedStore, declared);

            var result = await deleteController.Delete("MacLeod", "MacLeod", CancellationToken.None);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            var problem = Assert.IsType<ProblemDetails>(conflict.Value);
            Assert.Equal(SettingsProblemTypes.VersionConflict, problem.Type);
            Assert.Equal(409, problem.Status);

            // The rule the losing Delete tried to remove is exactly what a concurrent Update
            // would have left in place — proof nothing was silently clobbered either direction.
            var (rules, _) = PronunciationRuleJson.ParseDeclared(seedStore.ValueOf("Tts:Pronunciations"));
            Assert.Single(rules);
        }
    }
}
