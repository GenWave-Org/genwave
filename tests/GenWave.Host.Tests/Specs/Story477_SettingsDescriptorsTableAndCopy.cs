// STORY-477 — Settings descriptors table and copy (gh-#778 · SPEC F205.1–F205.3 · PLAN T572 T573 T574)
//
// BDD specification — xUnit. PLAN T572's five facts (AC1, AC2, AC3 ×2, AC4, AC13) are real bodies —
// AllowedSetting.Group/Min/Max/ChoiceSource now exist and SettingValidator reads ranges off the
// record (see StationSettingsAllowlist/SettingValidator's own remarks). Everything T573/T574 owns
// (label/help/group/range/choices copy, the localizer, the fallback culture, the missing-key boot
// guard) stays [Fact(Skip = ...)] with a loud body — remove the Skip only in the task that makes it
// green. Each Given comment names the arrange the scenario needs.

using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Host.Configuration;

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

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureSettingsdescriptorstableandcopy
{
    const string PendingT573 = "pending: T573 — Settings descriptors table and copy (STORY-477)";
    const string PendingT574 = "pending: T574 — Settings descriptors table and copy (STORY-477)";

    static async Task LoginAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story477SettingsWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

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

    public sealed class ScenarioTheLocalizer
    {
        // Given: IStringLocalizer<SettingsResources>, "Ads:BedDuckDb.Label", en (T573)

        /// <summary>AC5 — a non-empty plain label</summary>
        [Fact(Skip = PendingT573)]
        public void ServesTheLabel() => Assert.Fail(PendingT573);
    }

    public sealed class ScenarioGetSettingsInEnglish
    {
        // Given: GET /api/settings, Accept-Language: en (T574)

        /// <summary>AC6 — label present</summary>
        [Fact(Skip = PendingT574)]
        public void CarriesTheLabel() => Assert.Fail(PendingT574);

        /// <summary>AC6 — help present</summary>
        [Fact(Skip = PendingT574)]
        public void CarriesTheHelp() => Assert.Fail(PendingT574);

        /// <summary>AC6 — group {id:"sound", label:"Sound"}</summary>
        [Fact(Skip = PendingT574)]
        public void CarriesTheGroup() => Assert.Fail(PendingT574);

        /// <summary>AC6 — min −30 max 0</summary>
        [Fact(Skip = PendingT574)]
        public void CarriesTheRange() => Assert.Fail(PendingT574);

        /// <summary>AC7 — choices are {value,label}</summary>
        [Fact(Skip = PendingT574)]
        public void LabelsTheChoices() => Assert.Fail(PendingT574);
    }

    public sealed class ScenarioGetSettingsInAnUnknownCulture
    {
        // Given: Accept-Language: fr, no fr resx

        /// <summary>AC8 — en strings</summary>
        [Fact(Skip = PendingT573)]
        public void FallsBackToEnglish() => Assert.Fail(PendingT573);
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

    public sealed class ScenarioAKeyWithNoCopy
    {
        // Given: a test host whose resx lacks one key

        /// <summary>AC14 — boots and serves label = key</summary>
        [Fact(Skip = PendingT573)]
        public void DoesNotFailTheBoot() => Assert.Fail(PendingT573);
    }

}
