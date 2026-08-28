// STORY-151 — Every setting explains itself (Epic Y / SPEC F55.1, closes gitea-#231) — seeded-defaults
// half. The help-text/UI half lives in admin-ui/__specs__/settings-help-coverage.spec.tsx.
//
// BDD specification — xUnit. Y6 implements: appsettings.json seeds the Library:* defaults that
// previously existed only as C# property initializers, invisible to IConfiguration — GET
// /api/settings returned "" for them (the gitea-#231 root cause). The drift guard pins the seeded
// values equal to the options-class initializers so the two sources cannot diverge.
//
// Amended (Y6 smoke, 2026-07-15): a fresh-deploy smoke against the production binary found the
// SAME gitea-#231 root cause on six more keys the original four-key scope missed —
// Station:Rotation:RecentWindow/ArtistSeparation, Llm:TimeoutSeconds/MaxCopyChars,
// Tts:BlurbRetentionHours, Library:YearLookup:Enabled — ten seeded keys total (SPEC F55.1
// amended in place). Two of the eight blanks the smoke found, Llm:Endpoint and Llm:Model, are
// deliberately NOT seeded: their C# default IS empty (F34.2 — empty is the honest disabled
// state), so ScenarioFreshDeployHasNoLyingBlanks below excludes exactly those two.
//
// This file also carries FeatureSettingsHelpKeysParity (SPEC F55.3) — the C#-side half of the
// help-text coverage parity guard. Y6 owns no other xUnit spec file, and the mirror it guards
// (admin-ui/app/(authed)/settings/settings-help-keys.ts) is the TS-side anchor the jest
// settings-help-coverage.spec.tsx fixture is built from, so the two live together here rather
// than in a new file.

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Context.History;
using GenWave.Context.Weather;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Options;
using GenWave.Host.Tests.Fakes;
using GenWave.Loudness;
using GenWave.MediaLibrary.Options;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSeededDefaults
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Repo root, resolved relative to the test assembly's build output — the Story074/Story102/
    /// Story107 RepoRoot convention for reaching repo-root files from a test project.
    /// </summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string AppSettingsPath =>
        Path.Combine(RepoRoot, "src", "GenWave.Host", "appsettings.json");

    /// <summary>
    /// The real, on-disk configuration the api container loads at boot — the actual
    /// appsettings.json, no in-memory stand-ins — so these specs exercise the same file a fresh
    /// deploy reads.
    /// </summary>
    static IConfiguration RealAppSettingsConfig() =>
        new ConfigurationBuilder().AddJsonFile(AppSettingsPath, optional: false).Build();

    /// <summary>In-memory <see cref="IStationSettingsStore"/> — no overrides unless seeded via WriteAsync.</summary>
    sealed class FakeSettingsStore : IStationSettingsStore
    {
        readonly Dictionary<string, string> overrides = new(StringComparer.OrdinalIgnoreCase);

        public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
        {
            overrides[key] = value?.ToString() ?? string.Empty;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, string> result =
                new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(result);
        }
    }

    static SettingsController BuildController(IConfiguration config, IStationSettingsStore store) =>
        new(config, store, new SettingValidator(config), NullLogger<SettingsController>.Instance, new FakeIconPackStore())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    static async Task<List<SettingDto>> GetSettings(IConfiguration config, IStationSettingsStore store)
    {
        var controller = BuildController(config, store);
        var result = await controller.Get(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();
    }

    /// <summary>Reads a config value and asserts it is present and non-blank — never a lie via <c>!</c>.</summary>
    static string RequireValue(IConfiguration config, string key)
    {
        var value = config[key] ?? string.Empty;
        Assert.False(string.IsNullOrEmpty(value), $"expected a configured value for '{key}'.");
        return value;
    }

    /// <summary>
    /// Allowlisted keys whose C# default IS empty (<see cref="LlmOptions.Endpoint"/>/
    /// <see cref="LlmOptions.Model"/>, <see cref="StationOptions.PublicStreamUrl"/>,
    /// <see cref="TtsCorrectionsOptions.Corrections"/>) — empty is their honest disabled/unset
    /// state (F34.2 for the LLM pair; F62.8 for PublicStreamUrl, where empty means the spectator
    /// "about" panel hides the player; F68.5/F68.8 for Tts:Corrections, where empty means no
    /// operator corrections are configured — the MacLeod rule is demo-station SEED DATA in
    /// compose.demo.yaml, never a C# default), not a bug the F55.1 seeding contract covers.
    /// Tts:EngineByKind (SPEC F70.3, STORY-191) joins this set on the identical rationale: empty is
    /// its spec'd default (F70.3, "Default: empty map") — every kind falls through to the existing
    /// F70.1 health-based routing, and no compose topology needs to pin one. Tts:Pronunciations
    /// (SPEC F97.1, F97.3, STORY-253) joins on Tts:Corrections' own identical rationale: empty means
    /// no station pronunciation rules are configured, not a bug this contract covers.
    /// Station:Envelope:Genres (SPEC F81.1, STORY-212) joins on the same rationale: empty is its
    /// spec'd default ("empty Genres = all genres") — a fresh install's single station-default
    /// envelope constrains no genre until an operator narrows it, and no compose topology needs to
    /// pin one. Station:PublicBaseUrl (SPEC F88.4–F88.5, STORY-223, PLAN T85) joins on
    /// PublicStreamUrl's own identical rationale: empty is the honest default, and F88.5's whole
    /// contract IS that blank — no artwork URL is ever sent to a listening client until an
    /// operator sets one. Station:Timezone (gh-#117) joins on the same rationale: empty is its
    /// spec'd default — "use the container's own clock", the pre-gh-#117 behavior byte-identical —
    /// and seeding a concrete zone would silently repoint every fresh deploy's DJ clock.
    /// Station:Theme (SPEC F102.5, F102.14, STORY-265, PLAN T163 review hardening) joins on the
    /// SAME rationale, not the URL/free-text one: the visitor-cookie to settings-row to env-default
    /// to shipped-default precedence chain already terminates at
    /// <see cref="GenWave.Host.Theming.ThemeCatalog.ShippedDefaultSlug"/> without a config entry —
    /// seeding this key would duplicate that structural floor as an appsettings.json literal
    /// nothing enforces against the const it is copying, AND would permanently shadow F102.5's own
    /// "no value anywhere" branch so it never fires against a real deployment. Blank here is the
    /// chain having a floor, not a gap F55.1 exists to close.
    /// Context:Weather:PersonaId/Context:History:PersonaId (SPEC F107.7, PLAN T226) join on the
    /// SAME rationale as the URL/free-text pair below, not the boolean-default pair above: null and
    /// an absent key resolve identically (ContextProviderSettings' own remarks — "an unconfigured
    /// provider ... must resolve the same way" as an explicit 0), so an unset PersonaId is a
    /// deliberate, honest "defer to the on-air DJ", not a gap. Station:Location:Latitude/Longitude/
    /// SpokenName (SPEC F108.1, F108.3, PLAN T226) join on PublicStreamUrl's own identical
    /// rationale: blank means "no coordinate/place name configured", the correct fresh-deploy state
    /// until an operator sets one. Tts:Fallback:Endpoint/Voice (SPEC F99.2, F99.3, STORY-257, PLAN
    /// T148) join the set having MOVED here from <see cref="ComposeApiEnvMirror"/>: TTS failover
    /// became opt-in — the shipped compose.yaml no longer sets either key, so a fresh deploy's
    /// effective value really is blank now (an operator opts in with a live PUT /api/settings).
    /// Crosstalk:Shows (SPEC F127.8, STORY-328, PLAN T285) joins on Station:Envelope:Genres' own
    /// identical rationale, same shape (a JSON array of strings): empty IS the spec'd fail-closed
    /// default — the feature is off until an operator names a show, so seeding a value here would
    /// silently turn banter on for every fresh deploy. Station:IconPack (SPEC F130.4, STORY-337,
    /// PLAN T303) joins on Station:Theme's own identical rationale (not Crosstalk:Shows' fail-closed
    /// one): empty resolves to house icons — F130.4's own spec'd default, an icon pack has no
    /// "shipped" row a seed could even name — so a fresh deploy legitimately reports this key blank
    /// until an operator installs and activates a pack. Every other allowlisted key's C# default is
    /// non-empty.
    /// </summary>
    static readonly IReadOnlySet<string> HonestlyBlankKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Llm:Endpoint",
        "Llm:Model",
        "Station:PublicStreamUrl",
        "Station:PublicBaseUrl",
        "Tts:Corrections",
        "Tts:Pronunciations",
        "Tts:EngineByKind",
        "Tts:Fallback:Endpoint",
        "Tts:Fallback:Voice",
        "Station:Envelope:Genres",
        "Station:Timezone",
        "Station:Theme",
        "Station:IconPack",
        "Context:Weather:PersonaId",
        "Context:History:PersonaId",
        "Station:Location:Latitude",
        "Station:Location:Longitude",
        "Station:Location:SpokenName",
        "Crosstalk:Shows",
    };

    /// <summary>
    /// A real fresh deploy is <c>appsettings.json</c> PLUS the <c>api</c> service's
    /// <c>compose.yaml</c> environment — not <c>appsettings.json</c> alone. This mirrors exactly
    /// the settings-relevant allowlisted keys compose.yaml's <c>api</c> service supplies
    /// (Station:Name/Voice/Scope, Tts:Endpoint/RenderBudgetSeconds, Library:ScanIntervalSeconds/
    /// EnrichmentConcurrency, Loudness:TargetLufs/CeilingDbtp) so
    /// <see cref="ScenarioFreshDeployHasNoLyingBlanks"/> reproduces what the Y6 smoke actually ran
    /// against. Z9 (SPEC F63, STORY-160) pins this dictionary to compose.yaml with a two-way
    /// parity fact — <c>FeatureComposeEnvDriftGuard</c> in Story160_ComposeEnvDriftGuard.cs reads
    /// this exact field, so a compose.yaml env var added/removed/renamed on either side now fails
    /// loudly instead of drifting silently (closes gitea-#235).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string?> ComposeApiEnvMirror = new Dictionary<string, string?>
    {
        ["Station:Name"] = "GWAV 108.8",
        ["Station:Voice"] = "af_heart",
        ["Station:Scope:LibraryIds:0"] = "1",
        ["Tts:Endpoint"] = "http://kokoro:8880",
        ["Tts:RenderBudgetSeconds"] = "30",
        ["Library:ScanIntervalSeconds"] = "60",
        ["Library:EnrichmentConcurrency"] = "4",
        // Found drifted by the Z9 parity fact (2026-07-15): compose.yaml's api service has
        // carried Loudness__TargetLufs/CeilingDbtp since before this mirror existed, but neither
        // was ever added here. appsettings.json already seeds matching Loudness:* defaults
        // (-16/-1), so ScenarioFreshDeployHasNoLyingBlanks never caught the gap — the Z9 compose
        // fact is what closes it (F63.1).
        ["Loudness:TargetLufs"] = "-16",
        ["Loudness:CeilingDbtp"] = "-1",
        // Tts:Fallback:Endpoint/Voice REMOVED here 2026-08-14 (SPEC F99.2, F99.3, STORY-257, PLAN
        // T148): TTS failover became opt-in — compose.yaml's api service no longer sets either
        // key (the `piper` sidecar itself moved behind `profiles: ["fallback"]`, off by default),
        // so a genuine fresh deploy's effective value really is blank now. Moved to
        // HonestlyBlankKeys above, mirroring Llm:Endpoint's own "an honest blank" rationale.
    };

    static IConfiguration FreshDeployConfig() =>
        new ConfigurationBuilder()
            .AddJsonFile(AppSettingsPath, optional: false)
            .AddInMemoryCollection(ComposeApiEnvMirror)
            .Build();

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — the four blanks become real effective values
    // ─────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioTheFourKeysResolveTheirDefaults
    {
        [Fact]
        public async Task YearLookupEndpointResolvesTheSeededDefault()
        {
            var items = await GetSettings(RealAppSettingsConfig(), new FakeSettingsStore());

            var item = items.Single(i => i.Key.Equals(
                "Library:YearLookup:Endpoint", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("https://musicbrainz.org/ws/2", item.Value);
            Assert.Equal("default", item.Source);
        }

        [Fact]
        public async Task YearLookupMinScoreResolvesTheSeededDefault()
        {
            var items = await GetSettings(RealAppSettingsConfig(), new FakeSettingsStore());

            var item = items.Single(i => i.Key.Equals(
                "Library:YearLookup:MinScore", StringComparison.OrdinalIgnoreCase));

            Assert.Equal(90, int.Parse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture));
            Assert.Equal("default", item.Source);
        }

        [Fact]
        public async Task MinSilenceDurationSecResolvesTheSeededDefault()
        {
            var items = await GetSettings(RealAppSettingsConfig(), new FakeSettingsStore());

            var item = items.Single(i => i.Key.Equals(
                "Library:CueDetection:MinSilenceDurationSec", StringComparison.OrdinalIgnoreCase));

            Assert.Equal(0.5, double.Parse(item.Value, NumberStyles.Float, CultureInfo.InvariantCulture));
            Assert.Equal("default", item.Source);
        }

        [Fact]
        public async Task EnergyWindowSecondsResolvesTheSeededDefault()
        {
            var items = await GetSettings(RealAppSettingsConfig(), new FakeSettingsStore());

            var item = items.Single(i => i.Key.Equals(
                "Library:Energy:WindowSeconds", StringComparison.OrdinalIgnoreCase));

            Assert.Equal(12.0, double.Parse(item.Value, NumberStyles.Float, CultureInfo.InvariantCulture));
            Assert.Equal("default", item.Source);
        }
    }

    /// <summary>
    /// SPEC F55.1, amended (Y6 smoke, 2026-07-15) — the four-key scope above is exactly what
    /// masked the six-key regression: this scenario asserts the FULL contract instead, against
    /// the FULL fresh-deploy shape (<see cref="FreshDeployConfig"/>), so a future allowlist
    /// addition with an unseeded non-empty C# default fails here immediately.
    /// </summary>
    public sealed class ScenarioFreshDeployHasNoLyingBlanks
    {
        [Fact]
        public async Task NoAllowlistedKeyReturnsAnEmptyValueOnAFreshDeployExceptTheHonestLlmBlanks()
        {
            var items = await GetSettings(FreshDeployConfig(), new FakeSettingsStore());

            foreach (var allowed in StationSettingsAllowlist.All)
            {
                if (HonestlyBlankKeys.Contains(allowed.Key)) continue;

                var item = items.Single(i => i.Key.Equals(allowed.Key, StringComparison.OrdinalIgnoreCase));
                Assert.False(string.IsNullOrEmpty(item.Value),
                    $"'{allowed.Key}' must not be empty on a fresh deploy (SPEC F55.1) — " +
                    "the Y6 smoke found exactly this shape blank (gitea-#231).");
            }
        }

        [Fact]
        public async Task TheTwoHonestLlmBlanksStayEmptyOnAFreshDeploy()
        {
            // The mirror of the fact above: Llm:Endpoint/Llm:Model are SUPPOSED to be blank
            // (empty = disabled, F34.2) — asserting this explicitly guards against someone
            // "fixing" them into the seed by mistake, which would silently turn LLM copy on.
            var items = await GetSettings(FreshDeployConfig(), new FakeSettingsStore());

            foreach (var key in HonestlyBlankKeys)
            {
                var item = items.Single(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                Assert.Equal(string.Empty, item.Value);
            }
        }
    }

    public sealed class ScenarioSeedsEqualTheInitializers
    {
        [Fact]
        public void AppsettingsSeedsMatchTheOptionsClassInitializers()
        {
            var config = RealAppSettingsConfig();

            var yearLookupDefaults = new YearLookupOptions();
            var cueDetectionDefaults = new CueDetectionOptions();
            var energyDefaults = new EnergyOptions();
            var rotationDefaults = new StationRotationOptions();
            var llmDefaults = new LlmOptions();
            var ttsDefaults = new TtsOptions();

            // ── The original four F55.1 keys (closes gitea-#231) ────────────────────────────────────
            Assert.Equal(
                yearLookupDefaults.Endpoint,
                RequireValue(config, "Library:YearLookup:Endpoint"));

            Assert.Equal(
                yearLookupDefaults.MinScore,
                int.Parse(RequireValue(config, "Library:YearLookup:MinScore"), NumberStyles.Integer, CultureInfo.InvariantCulture));

            Assert.Equal(
                cueDetectionDefaults.MinSilenceDurationSec,
                double.Parse(RequireValue(config, "Library:CueDetection:MinSilenceDurationSec"), NumberStyles.Float, CultureInfo.InvariantCulture));

            Assert.Equal(
                energyDefaults.WindowSeconds,
                double.Parse(RequireValue(config, "Library:Energy:WindowSeconds"), NumberStyles.Float, CultureInfo.InvariantCulture));

            // ── The six keys the Y6 smoke found (same gitea-#231 root cause; F55.1 amended) ─────────
            Assert.Equal(
                yearLookupDefaults.Enabled,
                bool.Parse(RequireValue(config, "Library:YearLookup:Enabled")));

            Assert.Equal(
                rotationDefaults.RecentWindow,
                int.Parse(RequireValue(config, "Station:Rotation:RecentWindow"), NumberStyles.Integer, CultureInfo.InvariantCulture));

            Assert.Equal(
                rotationDefaults.ArtistSeparation,
                int.Parse(RequireValue(config, "Station:Rotation:ArtistSeparation"), NumberStyles.Integer, CultureInfo.InvariantCulture));

            Assert.Equal(
                llmDefaults.TimeoutSeconds,
                int.Parse(RequireValue(config, "Llm:TimeoutSeconds"), NumberStyles.Integer, CultureInfo.InvariantCulture));

            Assert.Equal(
                llmDefaults.MaxCopyChars,
                int.Parse(RequireValue(config, "Llm:MaxCopyChars"), NumberStyles.Integer, CultureInfo.InvariantCulture));

            // Llm:DegradationPin (SPEC F69.3, STORY-188) — seeded "auto" alongside the T32 feature
            // itself, closing the same gitea-#231 root cause before it can ever open for this key.
            Assert.Equal(
                llmDefaults.DegradationPin,
                RequireValue(config, "Llm:DegradationPin"));

            // Llm:ReasoningEffort (gh-#620) — seeded "none" alongside the reasoning control itself,
            // the same discipline as Llm:DegradationPin just above.
            Assert.Equal(
                llmDefaults.ReasoningEffort,
                RequireValue(config, "Llm:ReasoningEffort"));

            Assert.Equal(
                ttsDefaults.BlurbRetentionHours,
                int.Parse(RequireValue(config, "Tts:BlurbRetentionHours"), NumberStyles.Integer, CultureInfo.InvariantCulture));

            // llmDefaults.Endpoint/Model are deliberately NOT asserted here — they are the two
            // honest blanks (F34.2); ScenarioFreshDeployHasNoLyingBlanks pins that separately.

            // Station:Requests:* (SPEC F87.2, F87.6, STORY-224, PLAN T86) — seeded alongside the
            // feature itself, same "close gitea-#231 before it can ever open for this key" discipline
            // as Llm:DegradationPin above. Enabled's own default is false (0 is a legal, non-blank
            // seed — RequireValue only rejects an EMPTY value, not "false").
            var requestsDefaults = new StationRequestsOptions();
            Assert.Equal(
                requestsDefaults.Enabled,
                bool.Parse(RequireValue(config, "Station:Requests:Enabled")));
            Assert.Equal(
                requestsDefaults.OverrideEnvelope,
                bool.Parse(RequireValue(config, "Station:Requests:OverrideEnvelope")));
            Assert.Equal(
                requestsDefaults.WindowMinutes,
                int.Parse(RequireValue(config, "Station:Requests:WindowMinutes"), NumberStyles.Integer, CultureInfo.InvariantCulture));

            // Community:CatalogIndexUrl (SPEC F90.1, STORY-234, PLAN T99) — seeded alongside the
            // feature itself, same "close gitea-#231 before it can ever open for this key"
            // discipline as Llm:DegradationPin/Station:Requests:* above.
            Assert.Equal(
                new CommunityOptions().CatalogIndexUrl,
                RequireValue(config, "Community:CatalogIndexUrl"));

            // Station:Audience (SPEC F95.1, STORY-250, PLAN T111) — seeded alongside the feature
            // itself, same "close gitea-#231 before it can ever open for this key" discipline as
            // Community:CatalogIndexUrl just above.
            Assert.Equal(
                new StationOptions().Audience,
                RequireValue(config, "Station:Audience"));

            // The F107 context seam (SPEC F107.2/F108.2, F109.1, STORY-297, PLAN T226; F3 fix, T226
            // review) — Context:Weather:*/Context:History:* pinned against each provider's OWN
            // Default* consts (WeatherContextProvider/HistoryContextProvider's own remarks), not
            // ConfigurationContextSettingsProvider's generic fallback — that class stays
            // provider-agnostic by design (F4 fix), so it is never the right pin for a
            // provider-specific number like history's 240-minute default.
            Assert.Equal(
                WeatherContextProvider.DefaultEnabled,
                bool.Parse(RequireValue(config, "Context:Weather:Enabled")));
            Assert.Equal(
                WeatherContextProvider.DefaultSegmentCadenceMinutes,
                int.Parse(RequireValue(config, "Context:Weather:SegmentCadenceMinutes"), NumberStyles.Integer, CultureInfo.InvariantCulture));
            Assert.Equal(
                WeatherContextProvider.DefaultPatterCadenceMinutes,
                int.Parse(RequireValue(config, "Context:Weather:PatterCadenceMinutes"), NumberStyles.Integer, CultureInfo.InvariantCulture));
            Assert.Equal(
                HistoryContextProvider.DefaultEnabled,
                bool.Parse(RequireValue(config, "Context:History:Enabled")));
            Assert.Equal(
                HistoryContextProvider.DefaultSegmentCadenceMinutes,
                int.Parse(RequireValue(config, "Context:History:SegmentCadenceMinutes"), NumberStyles.Integer, CultureInfo.InvariantCulture));
            Assert.Equal(
                HistoryContextProvider.DefaultPatterCadenceMinutes,
                int.Parse(RequireValue(config, "Context:History:PatterCadenceMinutes"), NumberStyles.Integer, CultureInfo.InvariantCulture));

            // Station:Imaging:* (SPEC F110.1/F110.3, gh-#381, PLAN T226; F3 fix, T226 review) —
            // pinned against StationImagingOptions' own property defaults, the same
            // options-class-initializer discipline as every other pair above.
            var imagingDefaults = new StationImagingOptions();
            Assert.Equal(
                imagingDefaults.ClockAnchoredIdents,
                bool.Parse(RequireValue(config, "Station:Imaging:ClockAnchoredIdents")));
            Assert.Equal(
                imagingDefaults.TimeAnnouncements,
                bool.Parse(RequireValue(config, "Station:Imaging:TimeAnnouncements")));
            Assert.Equal(
                imagingDefaults.TimeAnnouncementBudgetSeconds,
                int.Parse(RequireValue(config, "Station:Imaging:TimeAnnouncementBudgetSeconds"), NumberStyles.Integer, CultureInfo.InvariantCulture));
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioOverridePrecedenceIsUntouched
    {
        [Fact]
        public async Task AnOverrideOnASeededKeyWinsAndReportsOverride()
        {
            // SettingsController.Get reads the effective VALUE from IConfiguration (populated by
            // StationSettingsConfigurationProvider, registered after appsettings.json so DB
            // overrides win) and the "source" label from the store separately — so a faithful
            // override test layers an in-memory override on top of the real appsettings.json,
            // exactly as the overlay provider would, alongside seeding the store for the source flag.
            var config = new ConfigurationBuilder()
                .AddJsonFile(AppSettingsPath, optional: false)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Library:YearLookup:MinScore"] = "95",
                })
                .Build();
            var store = new FakeSettingsStore();
            await store.WriteAsync("Library:YearLookup:MinScore", "95");

            var items = await GetSettings(config, store);

            var overridden = items.Single(i => i.Key.Equals(
                "Library:YearLookup:MinScore", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("override", overridden.Source);
            Assert.Equal("95", overridden.Value);

            // Seeding changed defaults, not precedence — sibling seeded keys stay "default".
            var sibling = items.Single(i => i.Key.Equals(
                "Library:YearLookup:Endpoint", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("default", sibling.Source);
        }
    }
}

/// <summary>
/// SPEC F55.3 (closes gitea-#230, gitea-#231) — the C#-side half of the settings-help-text coverage parity
/// guard. Jest cannot read <see cref="StationSettingsAllowlist"/> directly, so
/// <c>admin-ui/app/(authed)/settings/settings-help-keys.ts</c> carries an independently-authored
/// mirror of <see cref="StationSettingsAllowlist.All"/>'s key list; the jest
/// <c>settings-help-coverage.spec.tsx</c> spec builds its synthetic settings fixture from THAT
/// list and asserts every one of those keys renders help text. This fact is the other half: it
/// string-parses that same .ts file (the Story107/Story074/Story102 repo-content-fact idiom — no
/// TS toolchain runs inside the xUnit runner) and asserts its key list is equal, in order, to
/// <see cref="StationSettingsAllowlist.All"/> — so a key added to only ONE side (the C# allowlist,
/// or the TS mirror) fails a spec on BOTH toolchains, never a silent drift.
/// </summary>
public static class FeatureSettingsHelpKeysParity
{
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string SettingsHelpKeysTsPath =>
        Path.Combine(RepoRoot, "admin-ui", "app", "(authed)", "settings", "settings-help-keys.ts");

    /// <summary>
    /// Extracts the quoted string literals inside the <c>SETTINGS_HELP_KEYS</c> array literal
    /// only — bounded to that one array so a quoted word inside a doc comment elsewhere in the
    /// file can never leak into the parsed key list.
    /// </summary>
    static IReadOnlyList<string> ParseTsHelpKeyList()
    {
        var text = File.ReadAllText(SettingsHelpKeysTsPath);

        const string startMarker = "SETTINGS_HELP_KEYS = [";
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"could not find '{startMarker}' in {SettingsHelpKeysTsPath}");
        var arrayBodyStart = start + startMarker.Length;

        var end = text.IndexOf("] as const", arrayBodyStart, StringComparison.Ordinal);
        Assert.True(end >= 0, $"could not find the closing '] as const' in {SettingsHelpKeysTsPath}");

        var arrayBody = text[arrayBodyStart..end];
        return Regex.Matches(arrayBody, "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    public sealed class ScenarioTsMirrorMatchesTheAllowlist
    {
        [Fact]
        public void SettingsHelpKeysTsListsExactlyTheAllowlistKeysInOrder()
        {
            var tsKeys = ParseTsHelpKeyList();
            var allowlistKeys = StationSettingsAllowlist.All.Select(a => a.Key).ToList();

            Assert.Equal(allowlistKeys, tsKeys);
        }
    }
}
