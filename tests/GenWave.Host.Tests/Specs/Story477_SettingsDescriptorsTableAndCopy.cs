// STORY-477 — Settings descriptors table and copy (gh-#778 · SPEC F205.1–F205.3 · PLAN T572 T573 T574)
//
// BDD specification — xUnit. PLAN T572's five facts (AC1, AC2, AC3 ×2, AC4, AC13) are real bodies —
// AllowedSetting.Group/Min/Max/ChoiceSource now exist and SettingValidator reads ranges off the
// record (see StationSettingsAllowlist/SettingValidator's own remarks). PLAN T573's facts (AC5, AC14,
// plus its own SettingCopy-seam culture-fallback fact) are real bodies too — SettingsResources.resx,
// SettingsResources, SettingCopy, and the AddLocalization()/UseRequestLocalization() pipeline now
// exist (see those types' own remarks). PLAN T574's facts (AC6-AC8: the GET /api/settings DTO
// carrying label/help/group/range/choices copy, plus the fr-fallback and catalog-not-clobbered facts)
// are real bodies too — GenWave.Host.Api.SettingDto/SettingGroupDto and
// GenWave.Host.Api.SettingsController's BuildDto/LocalizedChoicesFor now exist (see those types' own
// remarks). Each Given comment names the arrange the scenario needs.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Theming;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes / WebApplicationFactory (AC4, AC13) ────────────────────────────────────────────

/// <summary>
/// Minimal <see cref="IStationSettingsStore"/> fake for the HTTP-level specs below — mirrors
/// Story058's <c>SafeScopeFakeSettingsStore</c> shape. <see cref="WriteIfVersionMatchesAsync"/> and
/// <see cref="ReadVersionsAsync"/> are not overridden: <see cref="IStationSettingsStore"/> already
/// gives both a default implementation, and neither AC4 (rejected before any store write) nor AC13
/// (an unconditional last-write-wins PUT) uses the version-guarded path.
/// </summary>
file sealed class Story477FakeSettingsStore : IStationSettingsStore
{
    readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        values[key] = value?.ToString() ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(values);
}

/// <summary>
/// Boots the real host for AC4/AC13's HTTP-level PUT specs (Story058's <c>SettingsApiWebFactory</c>/
/// Story185's <c>CorrectionsLiveWiringWebFactory</c> pattern) — real routing, cookie auth,
/// <see cref="GenWave.Host.Api.SettingsController"/>, <see cref="SettingValidator"/>, and
/// <see cref="StationSettingsAllowlist"/> are the genuine production wiring; only hosted services
/// and the Postgres-backed <see cref="IStationSettingsStore"/> are swapped out.
/// </summary>
file sealed class Story477SettingsWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t572";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint so
        // ValidateOnStart() is satisfied without injecting them manually (Story185's own finding).
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            // No Liquidsoap or DB connections during this test.
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IStationSettingsStore>();
            services.AddSingleton<IStationSettingsStore>(new Story477FakeSettingsStore());
        });
    }
}

/// <summary>
/// Wraps a real <see cref="IStringLocalizer{SettingsResources}"/> and reports exactly one name as
/// <see cref="LocalizedString.ResourceNotFound"/> (AC14, PLAN T573) — everything else passes straight
/// through to the real resx lookup, so this proves the missing-key path in isolation rather than
/// faking the whole localizer.
/// </summary>
file sealed class LabelMissingLocalizer : IStringLocalizer<SettingsResources>
{
    readonly IStringLocalizer<SettingsResources> inner;
    readonly string missingName;

    public LabelMissingLocalizer(IStringLocalizer<SettingsResources> inner, string missingName)
    {
        this.inner = inner;
        this.missingName = missingName;
    }

    public LocalizedString this[string name] =>
        name == missingName ? new LocalizedString(name, name, resourceNotFound: true) : inner[name];

    public LocalizedString this[string name, params object[] arguments] =>
        name == missingName
            ? new LocalizedString(name, name, resourceNotFound: true)
            : inner[name, arguments];

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        inner.GetAllStrings(includeParentCultures).Where(s => s.Name != missingName);
}

/// <summary>
/// Boots the real host with one resx entry deliberately missing (AC14, PLAN T573) — everything else
/// (routing, health check, <see cref="SettingCopy"/>'s own DI registration) is the genuine production
/// wiring; only <see cref="IStringLocalizer{SettingsResources}"/> is swapped for
/// <see cref="LabelMissingLocalizer"/>.
/// </summary>
file sealed class Story477MissingLabelWebFactory : WebApplicationFactory<Program>
{
    internal const string MissingLabelName = "Station:Ads:BedDuckDb.Label";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Story477SettingsWebFactory.Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IStationSettingsStore>();
            services.AddSingleton<IStationSettingsStore>(new Story477FakeSettingsStore());

            services.RemoveAll<IStringLocalizer<SettingsResources>>();
            services.AddSingleton<IStringLocalizer<SettingsResources>>(sp =>
            {
                var real = new StringLocalizer<SettingsResources>(sp.GetRequiredService<IStringLocalizerFactory>());
                return new LabelMissingLocalizer(real, MissingLabelName);
            });
        });
    }
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureSettingsdescriptorstableandcopy
{
    static async Task LoginAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story477SettingsWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    static readonly JsonSerializerOptions WireJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>GET /api/settings carrying <paramref name="acceptLanguage"/> as the request's own
    /// Accept-Language header (T574) — the real <c>RequestLocalizationMiddleware</c> resolves the
    /// UI culture <see cref="SettingCopy"/> reads copy against from this, same as a real client.
    /// Returns the raw response text alongside the deserialized DTOs — unlike
    /// <c>ReadFromJsonAsync</c>'s case-insensitive matching, the raw text lets a caller pin the
    /// wire's own literal camelCase property names, so a PascalCase regression would actually be
    /// caught.</summary>
    static async Task<(string RawBody, IReadOnlyList<SettingDto> Settings)> GetSettingsWithRawBodyAsync(
        HttpClient client, string acceptLanguage)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/settings");
        request.Headers.Add("Accept-Language", acceptLanguage);
        var response = await client.SendAsync(request);
        var rawBody = await response.Content.ReadAsStringAsync();
        var settings = JsonSerializer.Deserialize<IReadOnlyList<SettingDto>>(rawBody, WireJsonOptions)
            ?? throw new InvalidOperationException("GET /api/settings returned no body.");
        return (rawBody, settings);
    }

    static async Task<IReadOnlyList<SettingDto>> GetSettingsAsync(HttpClient client, string acceptLanguage) =>
        (await GetSettingsWithRawBodyAsync(client, acceptLanguage)).Settings;

    public sealed class ScenarioTheRecord
    {
        // Given: the AllowedSetting record for Loudness:TargetLufs
        readonly AllowedSetting setting = StationSettingsAllowlist.ByKey["Loudness:TargetLufs"];

        /// <summary>AC1 — carries Group, Min, Max, ChoiceSource</summary>
        [Fact]
        public void HasTheDescriptorFields() =>
            Assert.Equal(
                (SettingGroup.Sound, (double?)-40.0, (double?)0.0, SettingChoiceSource.Static),
                (setting.Group, setting.Min, setting.Max, setting.ChoiceSource));
    }

    public sealed class ScenarioEveryAllowlistEntry
    {
        // Given: StationSettingsAllowlist.All, and SettingValidator.cs's own source (repo root)

        static string RepoRoot =>
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        /// <summary>
        /// AC2 — every key grouped. Near-tautological for a required enum parameter — the compiler
        /// already refuses a call site that omits <see cref="AllowedSetting.Group"/> — so this fact's
        /// real job is guarding against a cast or a stray <see langword="default"/>(<see cref="SettingGroup"/>)
        /// slipping an undefined value past that structural guarantee.
        /// </summary>
        [Fact]
        public void GroupsEveryKey() =>
            Assert.All(StationSettingsAllowlist.All, s => Assert.True(Enum.IsDefined(s.Group)));

        /// <summary>AC3 — every Number key carries a usable Min/Max</summary>
        [Fact]
        public void RangesEveryNumber() =>
            Assert.All(
                StationSettingsAllowlist.All.Where(s => s.Kind == SettingKind.Number),
                s => Assert.True(s.Min.HasValue && s.Max.HasValue && s.Min.Value <= s.Max.Value));

        /// <summary>
        /// AC3 — no numeric range literal left in SettingValidator. Scans the real source file (not
        /// a compiled reflection trick) so a future PR that reintroduces a hardcoded range const
        /// fails this spec. <c>CrosstalkShowsMaxCount</c>/<c>AdsCastVoicesMaxCount</c> are excluded —
        /// list-count guards on String-kind keys, not Number-key ranges (PLAN T572 ruling: these two
        /// stay put).
        /// </summary>
        [Fact]
        public void LeavesNoRangeConstInTheValidator()
        {
            var source = File.ReadAllText(
                Path.Combine(RepoRoot, "src", "GenWave.Host", "Configuration", "SettingValidator.cs"));
            var nonRangeConsts = new HashSet<string>(StringComparer.Ordinal)
            {
                "CrosstalkShowsMaxCount",
                "AdsCastVoicesMaxCount",
            };

            var rangeConsts = Regex.Matches(source, @"internal const (?:int|double) (\w+)")
                .Select(m => m.Groups[1].Value)
                .Where(name => !nonRangeConsts.Contains(name));

            Assert.Empty(rangeConsts);
        }
    }

    public sealed class ScenarioTheValidator : IAsyncLifetime
    {
        // Given: PUT /api/settings Station:Ads:BedDuckDb = -61 (WebApplicationFactory)

        // Story477SettingsWebFactory is `file`-scoped, so it cannot appear in this public class's own
        // member signature (CS9051) — the field is typed as the base class instead.
        readonly WebApplicationFactory<Program> factory = new Story477SettingsWebFactory();
        HttpStatusCode status;
        string body = "";

        public async Task InitializeAsync()
        {
            var client = factory.CreateClient();
            await LoginAsync(client);

            var put = await client.PutAsJsonAsync("/api/settings", new[]
            {
                new { key = "Station:Ads:BedDuckDb", value = "-61" },
            });
            status = put.StatusCode;
            body = await put.Content.ReadAsStringAsync();
        }

        public async Task DisposeAsync() => await factory.DisposeAsync();

        /// <summary>
        /// AC4 — 400 naming the range. STORY-477's own AC4 text names -30 as BedDuckDb's floor; the
        /// shipped floor has always been -60 (see StationSettingsAllowlist's and
        /// AdLiveSettingsReader.MinBedDuckDb's own remarks) and PLAN T572 keeps it unchanged — a
        /// refactor onto the record, not a behaviour change. This spec PUTs -61, one past the REAL
        /// floor, instead of the story's -31; /plan owes AC4's text a rewrite to match (T572 ruling).
        /// </summary>
        [Fact]
        public void RefusesOutOfRangeFromTheRecord() =>
            Assert.Equal(
                (HttpStatusCode.BadRequest, true),
                (status, body.Contains("[-60, 0]", StringComparison.Ordinal)));
    }

    public sealed class ScenarioTheLocalizer : IAsyncLifetime
    {
        // Given: IStringLocalizer<SettingsResources>, "Station:Ads:BedDuckDb.Label", en (T573)

        // Story477SettingsWebFactory is `file`-scoped, so it cannot appear in this public class's own
        // member signature (CS9051) — the field is typed as the base class instead.
        readonly WebApplicationFactory<Program> factory = new Story477SettingsWebFactory();
        string label = "";

        public Task InitializeAsync()
        {
            var originalCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
                var localizer = factory.Services.GetRequiredService<IStringLocalizer<SettingsResources>>();
                label = localizer["Station:Ads:BedDuckDb.Label"];
            }
            finally
            {
                CultureInfo.CurrentUICulture = originalCulture;
            }
            return Task.CompletedTask;
        }

        public async Task DisposeAsync() => await factory.DisposeAsync();

        /// <summary>AC5 — a non-empty plain label, distinct from the raw resx name itself (the one
        /// expression that rules out both an empty string and a silent fall-through to the missing-key
        /// fallback).</summary>
        [Fact]
        public void ServesTheLabel() =>
            Assert.True(!string.IsNullOrEmpty(label) && label != "Station:Ads:BedDuckDb.Label");
    }

    public sealed class ScenarioGetSettingsInEnglish : IAsyncLifetime
    {
        // Given: GET /api/settings, Accept-Language: en (T574)

        const string BedDuckDbKey = "Station:Ads:BedDuckDb";
        const string ReasoningEffortKey = "Llm:ReasoningEffort";

        // The wire's own literal (case-sensitive) property names for BedDuckDb's copy fields —
        // sorted so the comparison in CarriesTheDocumentedWireShape doesn't
        // depend on the serializer's own property ordering.
        static readonly IReadOnlyList<string> BedDuckDbWireShapePaths =
            new[] { "group.id", "group.label", "help", "label", "max", "min" };

        /// <summary>
        /// Everything this scenario's facts assert against, arranged once in
        /// <see cref="InitializeAsync"/> — a typed holder instead of fields
        /// defaulted to <c>null!</c>: a fact that somehow ran before
        /// <see cref="InitializeAsync"/> would hit <see cref="Data"/>'s throwing accessor, never a
        /// silent null dereference.
        /// </summary>
        sealed record Fixture(
            SettingDto BedDuckDb,
            string ExpectedLabel,
            string ExpectedGroupLabel,
            IReadOnlyList<(string Value, string? Label)> ExpectedChoicePairs,
            IReadOnlyList<(string Value, string? Label)> ActualChoicePairs,
            IReadOnlyList<string> ActualBedDuckDbWirePaths);

        // Story477SettingsWebFactory is `file`-scoped, so it cannot appear in this public class's own
        // member signature (CS9051) — the field is typed as the base class instead.
        readonly WebApplicationFactory<Program> factory = new Story477SettingsWebFactory();
        Fixture? fixture;

        Fixture Data => fixture ?? throw new InvalidOperationException(
            $"{nameof(InitializeAsync)} did not run before this fact read {nameof(Data)}.");

        public async Task InitializeAsync()
        {
            // The expected en copy, read off the very same SettingCopy seam the controller uses
            // (T573) — never a second hard-coded copy of the resx text.
            // TryChoiceLabel (not ChoiceLabel) — ChoiceLabel's own value fallback would mask a
            // missing resx entry as a false match against a Choices row that also fell back to the
            // raw value, which would let AC7 pass vacuously.
            var settingCopy = factory.Services.GetRequiredService<SettingCopy>();
            string expectedLabel;
            string expectedGroupLabel;
            List<(string Value, string? Label)> expectedChoicePairs;
            var originalCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
                expectedLabel = settingCopy.Label(BedDuckDbKey);
                expectedGroupLabel = settingCopy.GroupLabel(SettingGroup.Sponsors);
                expectedChoicePairs = GenWave.Core.Llm.ReasoningEffort.Accepted
                    .Select(value => (Value: value, Label: settingCopy.TryChoiceLabel(ReasoningEffortKey, value)))
                    .ToList();
            }
            finally
            {
                CultureInfo.CurrentUICulture = originalCulture;
            }

            var client = factory.CreateClient();
            await LoginAsync(client);
            var (rawBody, settings) = await GetSettingsWithRawBodyAsync(client, "en");
            var bedDuckDb = settings.Single(s => s.Key.Equals(BedDuckDbKey, StringComparison.OrdinalIgnoreCase));
            var reasoningEffort = settings.Single(s => s.Key.Equals(ReasoningEffortKey, StringComparison.OrdinalIgnoreCase));

            fixture = new Fixture(
                BedDuckDb: bedDuckDb,
                ExpectedLabel: expectedLabel,
                ExpectedGroupLabel: expectedGroupLabel,
                ExpectedChoicePairs: expectedChoicePairs,
                ActualChoicePairs: (reasoningEffort.Choices ?? [])
                    .Select(choice => (choice.Value, (string?)choice.Label))
                    .ToList(),
                ActualBedDuckDbWirePaths: BedDuckDbWirePropertyPaths(rawBody, BedDuckDbKey));
        }

        public async Task DisposeAsync() => await factory.DisposeAsync();

        /// <summary>AC6 — label equals the resx en label for the key.</summary>
        [Fact]
        public void CarriesTheLabel() => Assert.Equal(Data.ExpectedLabel, Data.BedDuckDb.Label);

        /// <summary>AC6 — help present: non-empty, and not just the raw key echoed back.</summary>
        [Fact]
        public void CarriesTheHelp() =>
            Assert.True(!string.IsNullOrEmpty(Data.BedDuckDb.Help) && Data.BedDuckDb.Help != BedDuckDbKey);

        /// <summary>AC6 — group {id:"sponsors", label:"Sponsors"} (StationSettingsAllowlist's own
        /// SettingGroup for the key — not the story text's stale "sound", same drift its Min/Max own
        /// AC6 text has).</summary>
        [Fact]
        public void CarriesTheGroup() =>
            Assert.Equal(("sponsors", Data.ExpectedGroupLabel), (Data.BedDuckDb.Group.Id, Data.BedDuckDb.Group.Label));

        /// <summary>AC6 — range (-60, 0): the shipped floor (see StationSettingsAllowlist's own
        /// remarks — STORY-477's AC6 text still names the stale -30, /plan owes that rewrite).</summary>
        [Fact]
        public void CarriesTheRange() =>
            Assert.Equal(((double?)-60.0, (double?)0.0), (Data.BedDuckDb.Min, Data.BedDuckDb.Max));

        /// <summary>
        /// AC7 — every Llm:ReasoningEffort choice's label equals its own resx label.
        /// One <c>Assert.Equal</c> over the full projected (value, label) list, the expected side
        /// built from <c>TryChoiceLabel</c> — a null/empty <c>Choices</c> fails on a list-length
        /// mismatch instead of vacuously passing.
        /// </summary>
        [Fact]
        public void LabelsTheChoices() => Assert.Equal(Data.ExpectedChoicePairs, Data.ActualChoicePairs);

        /// <summary>
        /// N1 — the wire's copy fields use their documented camelCase property names
        /// (<c>label</c>, <c>help</c>, <c>group.id</c>, <c>group.label</c>, <c>min</c>, <c>max</c>),
        /// read off the raw response text rather than through <c>ReadFromJsonAsync</c>'s
        /// case-insensitive matching elsewhere in this file, which would not catch a PascalCase
        /// regression.
        /// </summary>
        [Fact]
        public void CarriesTheDocumentedWireShape() =>
            Assert.Equal(BedDuckDbWireShapePaths, Data.ActualBedDuckDbWirePaths);

        static IReadOnlyList<string> BedDuckDbWirePropertyPaths(string rawBody, string key)
        {
            using var document = JsonDocument.Parse(rawBody);
            var row = document.RootElement.EnumerateArray().Single(element => RowKeyIgnoreCase(element) == key);

            var paths = new List<string>();
            foreach (var name in new[] { "label", "help", "min", "max" })
                if (row.TryGetProperty(name, out _))
                    paths.Add(name);

            if (row.TryGetProperty("group", out var group))
                foreach (var name in new[] { "id", "label" })
                    if (group.TryGetProperty(name, out _))
                        paths.Add($"group.{name}");

            return paths.OrderBy(path => path, StringComparer.Ordinal).ToList();
        }

        // Locates the row by key case-insensitively — the SUT under test here is the copy fields'
        // own casing (N1), not the row-finder's, so this deliberately doesn't assume "key" itself
        // is camelCase.
        static string? RowKeyIgnoreCase(JsonElement row)
        {
            foreach (var property in row.EnumerateObject())
                if (string.Equals(property.Name, "key", StringComparison.OrdinalIgnoreCase))
                    return property.Value.GetString();
            return null;
        }
    }

    public sealed class ScenarioGetSettingsInAnUnknownCulture : IAsyncLifetime
    {
        // Given: Accept-Language: fr, no fr resx

        /// <summary>
        /// Everything this scenario's facts assert against, arranged once in
        /// <see cref="InitializeAsync"/>: the full en (key, label) sequence in allowlist order, the fr
        /// GET's (key, label) rows in response order, the keys served with an empty label, and the
        /// default theme choice's expected vs. served label.
        /// </summary>
        sealed record Fixture(
            IReadOnlyList<(string Key, string Label)> Expected,
            IReadOnlyList<(string Key, string Label)> Actual,
            string ExpectedThemeLabel,
            string? DefaultThemeChoiceLabel,
            IReadOnlyList<string> KeysWithEmptyLabel);

        // Story477SettingsWebFactory is `file`-scoped, so it cannot appear in this public class's own
        // member signature (CS9051) — the field is typed as the base class instead.
        readonly WebApplicationFactory<Program> factory = new Story477SettingsWebFactory();
        Fixture? fixture;

        Fixture Data => fixture ?? throw new InvalidOperationException(
            $"{nameof(InitializeAsync)} did not run before this fact read {nameof(Data)}.");

        public async Task InitializeAsync()
        {
            var expectedThemeLabel = ThemeCatalog.LoadShipped().All
                .Single(theme => theme.Slug == ThemeCatalog.ShippedDefaultSlug).Name;

            var client = factory.CreateClient();
            await LoginAsync(client);
            var frSettings = await GetSettingsAsync(client, "fr");

            // The en labels the fr response's own fallback ought to match, read straight off the
            // SettingCopy seam under an en UI culture (never through a second en GET) — this is the
            // expectation the fr response is checked against, not a mirror of it.
            var settingCopy = factory.Services.GetRequiredService<SettingCopy>();
            var originalCulture = CultureInfo.CurrentUICulture;
            IReadOnlyList<(string Key, string Label)> expected;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
                expected = StationSettingsAllowlist.All
                    .Select(entry => (entry.Key, Label: settingCopy.Label(entry.Key)))
                    .ToList();
            }
            finally
            {
                CultureInfo.CurrentUICulture = originalCulture;
            }

            var actual = frSettings.Select(s => (s.Key, s.Label)).ToList();

            var defaultThemeChoiceLabel = frSettings
                .SingleOrDefault(s => s.Key.Equals("Station:Theme", StringComparison.OrdinalIgnoreCase))
                ?.Choices?.SingleOrDefault(c => c.Value == ThemeCatalog.ShippedDefaultSlug)
                ?.Label;

            var keysWithEmptyLabel = frSettings
                .Where(s => string.IsNullOrEmpty(s.Label))
                .Select(s => s.Key)
                .ToList();

            fixture = new Fixture(
                Expected: expected,
                Actual: actual,
                ExpectedThemeLabel: expectedThemeLabel,
                DefaultThemeChoiceLabel: defaultThemeChoiceLabel,
                KeysWithEmptyLabel: keysWithEmptyLabel);
        }

        public async Task DisposeAsync() => await factory.DisposeAsync();

        /// <summary>AC8 — en strings, over the GET /api/settings HTTP surface (T574 wires the DTO to
        /// SettingCopy; ScenarioTheSettingCopyInAnUnknownCulture below proves the same fallback at the
        /// SettingCopy seam directly, T573's own share of this behaviour). One assert comparing the
        /// whole ordered key/label sequence: a missing row, an extra row, a reordering, or a single
        /// wrong label all surface as one Assert.Equal mismatch naming the exact expected-vs-actual
        /// difference, and an empty response no longer passes vacuously.</summary>
        [Fact]
        public void FallsBackToEnglish() => Assert.Equal(Data.Expected, Data.Actual);

        /// <summary>AC8 — Station:Theme's choice labels are catalog data (a theme's manifest name),
        /// never resx copy, so the fr fallback path must not clobber them into the raw slug.</summary>
        [Fact]
        public void KeepsCatalogSourcedChoiceLabelsUnclobbered() =>
            Assert.Equal(Data.ExpectedThemeLabel, Data.DefaultThemeChoiceLabel);

        /// <summary>AC8 "(no empty label)" — no fr row falls back to an empty label.
        /// One assert naming every offending key at once; the whole response being empty is
        /// already covered by <see cref="FallsBackToEnglish"/>'s length check.</summary>
        [Fact]
        public void NeverServesAnEmptyLabel() => Assert.Empty(Data.KeysWithEmptyLabel);
    }

    public sealed class ScenarioTheSettingCopyInAnUnknownCulture : IAsyncLifetime
    {
        // Given: SettingCopy.Label("Station:Ads:BedDuckDb"), en then fr, no fr resx (T573)

        // Story477SettingsWebFactory is `file`-scoped, so it cannot appear in this public class's own
        // member signature (CS9051) — the field is typed as the base class instead.
        readonly WebApplicationFactory<Program> factory = new Story477SettingsWebFactory();
        string enLabel = "";
        string frLabel = "";

        public Task InitializeAsync()
        {
            var settingCopy = factory.Services.GetRequiredService<SettingCopy>();
            var originalCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
                enLabel = settingCopy.Label("Station:Ads:BedDuckDb");

                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
                frLabel = settingCopy.Label("Station:Ads:BedDuckDb");
            }
            finally
            {
                CultureInfo.CurrentUICulture = originalCulture;
            }
            return Task.CompletedTask;
        }

        public async Task DisposeAsync() => await factory.DisposeAsync();

        /// <summary>T573 — no fr resx ships, so ResourceManager's own neutral-culture fallback serves
        /// the same label SettingCopy would return under en, proven at the SettingCopy seam directly
        /// rather than through GET /api/settings (T574's job).</summary>
        [Fact]
        public void FallsBackToTheEnglishLabel() => Assert.Equal(enLabel, frLabel);
    }

    public sealed class ScenarioTheExistingSettingsApi : IAsyncLifetime
    {
        // Given: PUT /api/settings Station:Ads:BedDuckDb = -12, an in-range value (WebApplicationFactory)
        // — the accept side of AC4's reject side, same moved range.

        // Story477SettingsWebFactory is `file`-scoped, so it cannot appear in this public class's own
        // member signature (CS9051) — the field is typed as the base class instead.
        readonly WebApplicationFactory<Program> factory = new Story477SettingsWebFactory();
        HttpStatusCode status;

        public async Task InitializeAsync()
        {
            var client = factory.CreateClient();
            await LoginAsync(client);

            var put = await client.PutAsJsonAsync("/api/settings", new[]
            {
                new { key = "Station:Ads:BedDuckDb", value = "-12" },
            });
            status = put.StatusCode;
        }

        public async Task DisposeAsync() => await factory.DisposeAsync();

        /// <summary>AC13 — an in-range PUT through the real HTTP pipeline still succeeds; Story043's
        /// own non-HTTP facts cover the rest of the existing PUT contract, unchanged.</summary>
        [Fact]
        public void KeepsPutGreen() => Assert.Equal(HttpStatusCode.OK, status);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAKeyWithNoCopy : IAsyncLifetime
    {
        // Given: a test host whose resx lacks "Station:Ads:BedDuckDb.Label" (LabelMissingLocalizer)

        readonly WebApplicationFactory<Program> factory = new Story477MissingLabelWebFactory();
        HttpStatusCode healthStatus;
        string label = "";

        public async Task InitializeAsync()
        {
            using var client = factory.CreateClient();
            var health = await client.GetAsync("/health");
            healthStatus = health.StatusCode;

            var settingCopy = factory.Services.GetRequiredService<SettingCopy>();
            label = settingCopy.Label("Station:Ads:BedDuckDb");
        }

        public async Task DisposeAsync() => await factory.DisposeAsync();

        /// <summary>AC14 — a missing resx entry never fails the boot.</summary>
        [Fact]
        public void BootsSuccessfully() => Assert.Equal(HttpStatusCode.OK, healthStatus);

        /// <summary>AC14 — a missing resx entry serves the key itself as the label.</summary>
        [Fact]
        public void ServesTheKeyAsTheLabel() => Assert.Equal("Station:Ads:BedDuckDb", label);
    }

}
