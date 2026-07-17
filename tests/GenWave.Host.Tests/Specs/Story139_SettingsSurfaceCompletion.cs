// STORY-139 — Every tunable in the console (Epic V / SPEC F44.2–F44.4, closes gitea-#197) — Host half.
// The scan/enrichment live-read half lives in MediaLibrary.Tests/Specs/Story139_LiveLibraryOptions.cs;
// the sections/badge UI half in admin-ui/__specs__/settings-new-sections.spec.tsx.
//
// BDD specification — xUnit. Implemented V8 (2026-07-14): six more keys join the Live allowlist
// (their boot-frozen consumers migrated to a provider/IOptionsMonitor read at use time — see
// Orchestrator/PlayHistoryService/TtsSegmentSource), two enrichment-mode keys join under the new
// SettingApplyMode.Enrichment, and the F44.4 exclusions are asserted absent.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Options;
using GenWave.Host.Playout;
using GenWave.Orchestration;

// Alias to disambiguate GenWave.Loudness (the analyzer project) from the Loudness domain type.
using CoreLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes (file-scoped: names are local to this spec) ───────────────────────────────

/// <summary>Minimal <see cref="IStationSettingsStore"/> double — mirrors Story100's FakeSettingsStore.</summary>
file sealed class FakeSettingsStore : IStationSettingsStore
{
    readonly Dictionary<string, string> overrides = new(StringComparer.OrdinalIgnoreCase);

    public int WriteCallCount { get; private set; }

    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
            throw new ArgumentException($"Key '{key}' is not allowlisted.", nameof(key));
        overrides[key] = value?.ToString() ?? string.Empty;
        WriteCallCount++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, string> result =
            new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(result);
    }
}

/// <summary>TTS segment source double whose render can be delayed to exercise the render-budget
/// timeout path — mirrors Orchestration.Tests' FakeTtsSegmentSource.RenderDelay (not reusable here:
/// test projects don't reference each other).</summary>
file sealed class DelayableTtsSegmentSource : ITtsSegmentSource
{
    public TimeSpan RenderDelay { get; set; } = TimeSpan.Zero;

    public async Task<MediaItem?> RenderAsync(SegmentRequest request, CancellationToken ct)
    {
        if (RenderDelay > TimeSpan.Zero)
            await Task.Delay(RenderDelay, ct);
        return new MediaItem("tts:seg", "/tts/seg.wav", request.StationName, new CoreLoudness(-16.0, -1.0, true));
    }
}

file sealed class NoOpActivePersonaAccessor : IActivePersonaAccessor
{
    public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);
}

public static class FeatureSettingsSurfaceCompletion
{
    static IConfiguration BuildConfig(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    static SettingsController BuildController(IConfiguration config, IStationSettingsStore store) =>
        new(
            config,
            store,
            new SettingValidator(config),
            NullLogger<SettingsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    // Minimal defaults for every F44.2/F44.3 key plus the pre-existing rotation pair — enough for
    // SettingsController.Get/Put to round-trip without touching the whole allowlist.
    static IEnumerable<KeyValuePair<string, string?>> NewKeyDefaults() =>
    [
        new("Tts:RenderBudgetSeconds",                    "30"),
        new("Tts:BlurbRetentionHours",                    "24"),
        new("Llm:MaxCopyChars",                           "450"),
        new("Admin:PlayHistoryCapacity",                  "50"),
        new("Library:ScanIntervalSeconds",                "60"),
        new("Library:EnrichmentConcurrency",              "4"),
        new("Library:CueDetection:MinSilenceDurationSec", "0.5"),
        new("Library:Energy:WindowSeconds",                "12"),
        new("Station:Rotation:RecentWindow",              "20"),
        new("Station:Rotation:ArtistSeparation",          "2"),
    ];

    public sealed class ScenarioTheNewLiveKeysArePresentAndValidated
    {
        static readonly string[] NewLiveKeys =
        [
            "Tts:RenderBudgetSeconds",
            "Tts:BlurbRetentionHours",
            "Llm:MaxCopyChars",
            "Admin:PlayHistoryCapacity",
            "Library:ScanIntervalSeconds",
            "Library:EnrichmentConcurrency",
        ];

        [Fact]
        public void EveryNewLiveKeyAppearsInTheAllowlistWithLiveApplyMode()
        {
            foreach (var key in NewLiveKeys)
            {
                Assert.True(StationSettingsAllowlist.ByKey.TryGetValue(key, out var allowed),
                    $"{key} is missing from the allowlist");
                Assert.Equal(SettingApplyMode.Live, allowed.ApplyMode);
            }
        }

        [Fact]
        public void EveryNewKeyHasAWorkingValidatorEntry()
        {
            var validator = new SettingValidator(BuildConfig(NewKeyDefaults()));

            // A valid value passes …
            Assert.Null(validator.Validate("Tts:RenderBudgetSeconds", "30"));
            Assert.Null(validator.Validate("Tts:BlurbRetentionHours", "24"));
            Assert.Null(validator.Validate("Llm:MaxCopyChars", "450"));
            Assert.Null(validator.Validate("Admin:PlayHistoryCapacity", "50"));
            Assert.Null(validator.Validate("Library:ScanIntervalSeconds", "60"));
            Assert.Null(validator.Validate("Library:EnrichmentConcurrency", "4"));

            // … and a floor-violating value for each is rejected (F44.2's "working validator").
            Assert.NotNull(validator.Validate("Tts:RenderBudgetSeconds", "0"));
            Assert.NotNull(validator.Validate("Tts:BlurbRetentionHours", "0"));
            Assert.NotNull(validator.Validate("Llm:MaxCopyChars", "0"));
            Assert.NotNull(validator.Validate("Admin:PlayHistoryCapacity", "0"));
            Assert.NotNull(validator.Validate("Library:ScanIntervalSeconds", "0"));
            Assert.NotNull(validator.Validate("Library:EnrichmentConcurrency", "0"));
        }

        [Fact]
        public async Task TheOrchestratorRenderBudgetReadsTheMonitorNotAFrozenTimeSpan()
        {
            // A boot-frozen TimeSpan (the pre-V8 design) would keep dropping the segment forever
            // once constructed short — this proves the SAME Orchestrator instance honors a live
            // widen with no re-construction (SPEC F44.2, mirrors Story135's rotation-depth proof).
            var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
            var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
            var cadenceProvider = new FakeCadenceProvider(new CadenceConfig
            {
                LeadInBeforeEachTrack = true,
                BackAnnounceAfterEachTrack = false,
                StationIdEveryNUnits = 0,
            });
            var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
            var catalog = new FakeMediaCatalog(new MediaReference(
                "m1", "/media/m1.mp3", "Track 1", new CoreLoudness(-23.0, -1.0, true),
                null, null, null, null, null, null, null, null));
            var tts = new DelayableTtsSegmentSource { RenderDelay = TimeSpan.FromMilliseconds(300) };
            var budgetProvider = new FakeRenderBudgetProvider(TimeSpan.FromMilliseconds(20));

            var orchestrator = new Orchestrator(
                identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog, tts,
                new NoOpActivePersonaAccessor(), NullLogger<Orchestrator>.Instance, budgetProvider);
            var ctx = new PlayoutContext([]);

            // Unit 1 — budget (20ms) is far shorter than the render delay (300ms): the lead-in is
            // dropped and the first pulled item is music.
            var firstItem = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            Assert.NotNull(firstItem);
            Assert.False(firstItem.MediaId.StartsWith("tts:", StringComparison.Ordinal));

            // The live edit: no re-construction, no restart — same provider instance, new value.
            budgetProvider.Budget = TimeSpan.FromSeconds(5);

            // Unit 2 — the SAME orchestrator, now with a budget comfortably longer than the
            // render delay: the lead-in succeeds and is pulled first.
            var secondItem = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            Assert.NotNull(secondItem);
            Assert.StartsWith("tts:", secondItem.MediaId, StringComparison.Ordinal);
        }

        [Fact]
        public void ShrinkingPlayHistoryCapacityTrimsTheRingOnTheNextPush()
        {
            var options = new FakeOptionsMonitor<AdminOptions>(new AdminOptions { PlayHistoryCapacity = 50 });
            var history = new PlayHistoryService(options);
            var now = DateTimeOffset.UtcNow;

            for (var i = 0; i < 5; i++)
                history.Push(new PlayHistoryEntry("s1", $"m{i}", null, null, 0, now.AddSeconds(i), null, null));

            Assert.Equal(5, history.GetEntries("s1").Count);

            // The live edit: no re-construction — same service instance, new capacity.
            options.CurrentValue = new AdminOptions { PlayHistoryCapacity = 3 };
            history.Push(new PlayHistoryEntry("s1", "m5", null, null, 0, now.AddSeconds(5), null, null));

            // The ring trims to the NEW capacity on this very push — no api restart (AC2).
            Assert.Equal(3, history.GetEntries("s1").Count);
        }

        [Fact]
        public void RotationKeysShareTheSameAllowlistAndValidatorMechanismAsTheNewLiveKeys()
        {
            // V4 reviewer follow-up (folded into V8): Station:Rotation:RecentWindow/ArtistSeparation
            // joined the allowlist in V4 (SPEC F41.6) via the identical mechanism this task extends —
            // pinned here alongside the new membership facts so the two families never drift apart.
            Assert.True(StationSettingsAllowlist.ByKey.TryGetValue(
                "Station:Rotation:RecentWindow", out var recentWindow));
            Assert.Equal(SettingApplyMode.Live, recentWindow.ApplyMode);
            Assert.True(StationSettingsAllowlist.ByKey.TryGetValue(
                "Station:Rotation:ArtistSeparation", out var artistSeparation));
            Assert.Equal(SettingApplyMode.Live, artistSeparation.ApplyMode);

            var validator = new SettingValidator(BuildConfig(NewKeyDefaults()));
            Assert.Null(validator.Validate("Station:Rotation:RecentWindow", "0"));      // 0 legally disables
            Assert.NotNull(validator.Validate("Station:Rotation:RecentWindow", "-1"));  // negative rejected
            Assert.Null(validator.Validate("Station:Rotation:ArtistSeparation", "0"));
            Assert.NotNull(validator.Validate("Station:Rotation:ArtistSeparation", "-1"));
        }
    }

    public sealed class ScenarioEnrichmentInputsBadgeHonestly
    {
        [Fact]
        public void TheApplyModeEnumCarriesAnEnrichmentValue()
        {
            Assert.True(Enum.IsDefined(typeof(SettingApplyMode), SettingApplyMode.Enrichment));
            // Distinct from the other two — never conflated at the wire (F44.3).
            Assert.NotEqual(SettingApplyMode.Live, SettingApplyMode.Enrichment);
            Assert.NotEqual(SettingApplyMode.EngineRestart, SettingApplyMode.Enrichment);
        }

        [Fact]
        public async Task BothEnrichmentTimeKeysCarryTheEnrichmentApplyMode()
        {
            Assert.True(StationSettingsAllowlist.ByKey.TryGetValue(
                "Library:CueDetection:MinSilenceDurationSec", out var minSilence));
            Assert.Equal(SettingApplyMode.Enrichment, minSilence.ApplyMode);
            Assert.True(StationSettingsAllowlist.ByKey.TryGetValue(
                "Library:Energy:WindowSeconds", out var window));
            Assert.Equal(SettingApplyMode.Enrichment, window.ApplyMode);

            // The wire value the admin UI badges on (SPEC F44.3's "applies at next enrichment").
            var config = BuildConfig(NewKeyDefaults());
            var controller = BuildController(config, new FakeSettingsStore());
            var result = await controller.Get(CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();

            var minSilenceDto = items.Single(i => i.Key == "Library:CueDetection:MinSilenceDurationSec");
            Assert.Equal("enrichment", minSilenceDto.ApplyMode);
            var windowDto = items.Single(i => i.Key == "Library:Energy:WindowSeconds");
            Assert.Equal("enrichment", windowDto.ApplyMode);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioTheExclusionsStayOut
    {
        [Fact]
        public void NoStationSafeKeyIsAllowlisted()
        {
            // "Station:Safe:*" (the F27.10 generation-time inputs) is a distinct prefix from the
            // already-allowlisted "Station:SafeScope:*" — StartsWith below only matches the former.
            Assert.DoesNotContain(StationSettingsAllowlist.All,
                s => s.Key.StartsWith("Station:Safe:", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void TheSilenceThresholdIsNotAllowlisted()
        {
            // Locked to the engine's hardcoded blank.eat threshold (F13.7) — a live edit would
            // silently violate that invariant.
            Assert.False(StationSettingsAllowlist.ByKey.ContainsKey("Library:CueDetection:SilenceThresholdDb"));
        }

        [Fact]
        public void NoSecretIsAllowlisted()
        {
            Assert.False(StationSettingsAllowlist.ByKey.ContainsKey("Admin:Password"));
            Assert.False(StationSettingsAllowlist.ByKey.ContainsKey("Llm:ApiKey"));
            Assert.DoesNotContain(StationSettingsAllowlist.All,
                s => s.Key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void SessionLifetimeIsNotAllowlisted()
        {
            // Boot-wired into the auth cookie stack (Program.cs reads it once at startup) —
            // negligible tuning value (F44.4).
            Assert.False(StationSettingsAllowlist.ByKey.ContainsKey("Admin:SessionLifetimeHours"));
        }
    }

    public sealed class ScenarioInvalidValuesAreRejectedPerKey
    {
        [Fact]
        public async Task AnOutOfRangeValueForANewKeyIs400AndUnpersisted()
        {
            var config = BuildConfig(NewKeyDefaults());
            var store = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest> { new("Admin:PlayHistoryCapacity", "0") };
            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }
    }
}
