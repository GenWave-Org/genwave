// STORY-149 — Settings that cannot be fat-fingered into instability (Epic Y / SPEC F53,
// closes gitea-#221).
//
// BDD specification — xUnit. Implemented Y4 (2026-07-15): every F53.1 ceiling joins the
// SettingValidator's existing floors (inclusive both bounds); BuildRangeError copy names both.
// Boot validation is deliberately NOT tightened (F53.2) — mirrors the V6 idiom in
// Story136_StationIdCadenceValidation: StationOptionsValidator, not the nested [Range]
// attribute, is the seam that actually runs at ValidateOnStart, and it stays floor-only. F53.4
// (no retroactive clamp) is pinned against the same FakeSettingsStore/BuildController idiom as
// Story139_SettingsSurfaceCompletion.

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes (file-scoped: names are local to this spec) ───────────────────────────────

/// <summary>Minimal <see cref="IStationSettingsStore"/> double — mirrors Story139's own FakeSettingsStore.</summary>
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

public static class FeatureSettingCeilings
{
    /// <summary>How far below an exclusive-zero floor's ceiling a "legal but tiny" value sits —
    /// used to prove the exclusive floor (still &gt; 0) survives the F53.1 ceiling addition
    /// without picking an arbitrary constant that could collide with a key's own ceiling.</summary>
    const string TinyPositive = "0.5";

    /// <summary>
    /// The F53.1 ceiling table (closes gitea-#221) — one row per key, carrying: a value AT the floor
    /// (still legal, floors are unchanged), the ceiling itself (AT the ceiling, legal — inclusive),
    /// a value strictly ABOVE the ceiling (rejected), and the literal floor bound as it appears in
    /// BuildRangeError's copy (0 or 1 for every key here — no key in this table has a non-zero/one
    /// floor bound in its rejection message).
    ///
    /// AboveCeiling is deliberately chosen to share NO digits with Ceiling (e.g. "99" above a "30"
    /// or "60" ceiling, "999" above a "600" ceiling) — the rejection message echoes the rejected
    /// input verbatim, so a probe like "30.5" would let RejectionMessagesNameBothBounds' ceiling-
    /// token regex match the echoed input instead of the copy's own "at most 30" bound (review
    /// finding, Y4 hardening).
    /// </summary>
    static readonly (string Key, string FloorValue, string FloorBoundInCopy, string Ceiling, string AboveCeiling)[] CeilingTable =
    [
        ("Library:EnrichmentConcurrency",              "1",           "1", "32",    "33"),
        ("Admin:PlayHistoryCapacity",                  "1",           "1", "5000",  "5001"),
        ("Station:Rotation:RecentWindow",               "0",           "0", "10000", "10001"),
        ("Station:Rotation:ArtistSeparation",           "0",           "0", "100",   "101"),
        ("Station:Cadence:StationIdEveryNUnits",        "0",           "0", "1000",  "1001"),
        ("Library:ScanIntervalSeconds",                 "1",           "1", "86400", "86401"),
        ("Tts:RenderBudgetSeconds",                     "1",           "1", "600",   "601"),
        ("Tts:BlurbRetentionHours",                     "1",           "1", "8760",  "8761"),
        ("Llm:MaxCopyChars",                            "1",           "1", "10000", "10001"),
        ("Llm:TimeoutSeconds",                          "1",           "1", "300",   "301"),
        // Exclusive floor (0 is illegal) — FloorValue is a tiny-but-legal stand-in, not 0 itself.
        ("GW_XFADE_MIN",                                TinyPositive,  "0", "30",    "99"),
        ("GW_XFADE_MAX",                                TinyPositive,  "0", "30",    "99"),
        ("GW_SAFE_GAP_SECONDS",                         "0",           "0", "600",   "999"),
        ("Library:CueDetection:MinSilenceDurationSec",  TinyPositive,  "0", "60",    "99"),
        ("Library:Energy:WindowSeconds",                TinyPositive,  "0", "60",    "99"),
    ];

    static SettingValidator BuildValidator() =>
        new(new ConfigurationBuilder().Build());

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

    /// <summary>
    /// A number token is "named" in an error message when it appears SOMEWHERE with no adjacent
    /// digit on either side — scanning every position (not just the first) matters because the
    /// message also echoes the rejected input value verbatim (e.g. an "Admin:PlayHistoryCapacity"
    /// rejection for "5001" contains a "1" inside that echoed value; the floor "1" this asserts on
    /// is a DIFFERENT, later "1" — the one in "between 1 and 5000").
    /// </summary>
    static void AssertNamesNumber(string message, string token)
    {
        var pattern = $@"(?<!\d){Regex.Escape(token)}(?!\d)";
        Assert.True(Regex.IsMatch(message, pattern),
            $"Expected '{token}' to appear as a standalone number in error message: \"{message}\"");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — the F53.1 table binds the settings API
    // ─────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioEveryCeilingInTheTableIsEnforced
    {
        [Fact]
        public void EveryF531KeyRejectsAValueAboveItsCeiling()
        {
            var validator = BuildValidator();

            foreach (var row in CeilingTable)
            {
                Assert.NotNull(validator.Validate(row.Key, row.AboveCeiling));
            }
        }

        [Fact]
        public void RejectionMessagesNameBothBounds()
        {
            var validator = BuildValidator();

            foreach (var row in CeilingTable)
            {
                var error = validator.Validate(row.Key, row.AboveCeiling);
                Assert.NotNull(error);
                AssertNamesNumber(error, row.FloorBoundInCopy);
                AssertNamesNumber(error, row.Ceiling);
            }
        }

        [Fact]
        public void ValuesAtTheCeilingAreAcceptedInclusive()
        {
            var validator = BuildValidator();

            foreach (var row in CeilingTable)
            {
                Assert.Null(validator.Validate(row.Key, row.Ceiling));
            }
        }

        [Fact]
        public void ValuesAtTheFloorStillPassUnchanged()
        {
            var validator = BuildValidator();

            foreach (var row in CeilingTable)
            {
                Assert.Null(validator.Validate(row.Key, row.FloorValue));
            }
        }

        [Fact]
        public async Task AnOverCeilingPutPersistsNothing()
        {
            var config     = BuildConfig([new("Library:EnrichmentConcurrency", "4")]);
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("Library:EnrichmentConcurrency", "33"), // one above the F53.1 ceiling (32)
            };
            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioBootIsNeverBrickedByACeiling
    {
        static StationOptions ValidOptions() => new()
        {
            Id    = "s1",
            Name  = "GenWave",
            Voice = "af_heart",
            Scope = new StationScopeOptions { LibraryIds = [1L] },
        };

        static StationOptionsValidator BuildStationOptionsValidator() =>
            new(NullLogger<StationOptionsValidator>.Instance);

        [Fact]
        public void AnOverCeilingEnvValuePassesBootValidation()
        {
            // Station:Rotation:RecentWindow's F53.1 ceiling is 10000 (settings-API-only) — this
            // value would be rejected by SettingValidator.Validate, but boot validation is
            // deliberately NOT tightened to match (F53.2). StationOptionsValidator is the seam
            // that actually runs at ValidateOnStart (Program.cs) — the nested [Range(0,
            // int.MaxValue)] attribute on StationRotationOptions is documentation only and is
            // never evaluated at boot (the V6 lesson, Story136_StationIdCadenceValidation).
            var options = ValidOptions();
            options.Rotation.RecentWindow = 999999;

            var result = BuildStationOptionsValidator().Validate(null, options);

            Assert.True(result.Succeeded);
        }
    }

    public sealed class ScenarioAPreExistingOverCeilingOverrideDegradesGracefully
    {
        const string Key = "Admin:PlayHistoryCapacity"; // F53.1 ceiling: 5000
        const string OverCeilingValue = "50000";

        // Seeding is inlined per-fact (rather than factored into a shared helper) because the
        // fake store's WriteCallCount is only visible on the file-local FakeSettingsStore type
        // itself, and a private method of a non-file-local nested class cannot expose a
        // file-local type in its own signature (CS9051).
        //
        // Simulates a DB override row written before this ceiling existed (or written directly
        // against the store, bypassing the API): both the effective config value AND the store
        // carry the same over-ceiling value, mirroring Story043's own "source=override" idiom
        // where GET's displayed value always comes from the merged IConfiguration and the store
        // only flips the source label.

        [Fact]
        public async Task GetStillReturnsAStoredOverCeilingOverride()
        {
            var config = BuildConfig([new(Key, OverCeilingValue)]);
            var store  = new FakeSettingsStore();
            await store.WriteAsync(Key, OverCeilingValue, CancellationToken.None);
            var controller = BuildController(config, store);

            var result = await controller.Get(CancellationToken.None);

            var ok    = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();
            var item  = items.Single(i => i.Key == Key);

            Assert.Equal(OverCeilingValue, item.Value);
            Assert.Equal("override", item.Source);
        }

        [Fact]
        public async Task RePuttingTheSameOverCeilingValueIsRejected()
        {
            var config = BuildConfig([new(Key, OverCeilingValue)]);
            var store  = new FakeSettingsStore();
            await store.WriteAsync(Key, OverCeilingValue, CancellationToken.None);
            var writesBeforePut = store.WriteCallCount;
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest> { new(Key, OverCeilingValue) };
            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(writesBeforePut, store.WriteCallCount);
        }
    }
}
