// STORY-380 — The knobs and the laws (SPEC F155 · PLAN T357, T366)
//
// BDD specification — xUnit. AC1/AC6 (T357) drive the REAL production binary
// (WebApplicationFactory<Program>, the Story084/Story125 "fake ConnectionStrings:Library, real
// composition root" idiom — no live Postgres needed: GardenerOptions is bound and boot-validated
// purely off IConfiguration, with no repository/table read on the path either AC exercises) reading
// GardenerOptions off the booted host's DI container. AC2/AC5 (T366) drive Station:Thumbs:Enabled
// through the real PUT /api/settings surface, which DOES need the ephemeral station+library Postgres
// (tests/GenWave.Host.Tests/Support/EphemeralStationDatabase) — left skipped here for T366 to build.
// AC3 (the L5 reserved-namespace pin) and AC4 (the three-way disjointness pin) live in
// GenWave.Architecture.Tests, written by another agent — not this file.
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using GenWave.MediaLibrary.Options;

namespace GenWave.Host.Tests.Specs;

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Boots the real production composition root (Program.cs) with a fake, unreachable
/// <c>ConnectionStrings:Library</c> — the Story084/Story125 idiom (<c>StatusApiWebFactory</c>/
/// <c>LlmStatusWebFactory</c>): every option class Program.cs binds is genuinely wired and
/// boot-validated, but nothing on this file's own two ACs ever queries Postgres (GardenerOptions'
/// own <c>ValidateOnStart()</c> reads only IConfiguration), so a real ephemeral database would be
/// pure overhead here. <paramref name="gardenerOverrides"/> mirrors <c>Gardener__*</c> env vars as
/// colon-form <c>UseSetting</c> keys — a per-instance value with no shared process state (the
/// EnvVarMutatingWebFactoryCollection precedent: every spec that can reach Program.cs's
/// composition-time config reads this way does, reserving real env-var mutation for the one factory
/// that genuinely needs to prove ABSENCE).
/// </summary>
file sealed class GardenerKnobsWebFactory(params (string Key, string Value)[] gardenerOverrides)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story380-knobs";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint so
        // ValidateOnStart() is satisfied without injecting them manually (the Story084/Story125
        // precedent this file's own header names).
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        foreach (var (key, value) in gardenerOverrides)
            builder.UseSetting($"Gardener:{key}", value);

        builder.ConfigureTestServices(services =>
        {
            // No Liquidsoap/DB-touching background loop during this test — mirrors every other
            // WebApplicationFactory-based spec in this suite (ScanService/EnrichmentService would
            // otherwise start against the fake connection string above).
            services.RemoveAll<IHostedService>();
        });
    }
}

// ---------------------------------------------------------------------
// HAPPY PATH — sane defaults, and the one Live knob flips without a restart
// ---------------------------------------------------------------------

public static class FeatureTheKnobsAndTheLiveSwitch
{
    public sealed class ScenarioDefaults
    {
        // Given no Gardener__* env, When the api boots.
        [Fact]
        public void TheBoundOptionsMatchTheDocumentedDefaults()
        {
            using var factory = new GardenerKnobsWebFactory();

            var options = factory.Services.GetRequiredService<IOptions<GardenerOptions>>().Value;

            Assert.Equal(0.5, options.NudgeGain);
            Assert.Equal(30, options.HalfLifeDays);
            Assert.Equal(5, options.Saturation);
            Assert.Equal(30, options.ThumbCooldownSeconds);
            Assert.Equal(60, options.ThumbDailyCap);
            Assert.Equal(90, options.ThumbRetentionDays);
            Assert.Equal(90, options.ShelfDustDays);
            Assert.Equal(2000, options.DuplicateToleranceMs);
            Assert.Equal(60, options.IntervalMinutes);
            Assert.Equal(500, options.BatchSize);
            Assert.False(options.FileActions.Enabled);
        }

        // LOW-5 (T357 review): FileActions.Enabled == false is indistinguishable, on its own, from
        // the nested Gardener:FileActions section never binding at all — this fact proves the nested
        // object genuinely binds a live config value rather than always reporting its C# default.
        [Fact]
        public void FileActionsEnabledGenuinelyBindsFromTheNestedSection()
        {
            using var factory = new GardenerKnobsWebFactory(("FileActions:Enabled", "true"));

            var options = factory.Services.GetRequiredService<IOptions<GardenerOptions>>().Value;

            Assert.True(options.FileActions.Enabled);
        }
    }

    public sealed class ScenarioTheLiveSwitch
    {
        // Given Station:Thumbs:Enabled false, When PUT /api/settings sets it true.
        [Fact(Skip = "pending T366 (STORY-380 AC2)")]
        public void TheNextThumbPostIsAcceptedWithNoRestart() => Assert.Fail("pending T366");
    }

    public sealed class ScenarioDisclosure
    {
        // Given thumbs enabled, When the F67 disclosure suites run.
        [Fact(Skip = "pending T366 (STORY-380 AC5)")]
        public void TheNowPlayingContractIsThePinnedSetPlusAiring() => Assert.Fail("pending T366");

        [Fact(Skip = "pending T366 (STORY-380 AC5)")]
        public void TheThumbsTwoOhTwoBodyIsThePinnedConstant() => Assert.Fail("pending T366");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a bad knob never reaches a running station
    // ---------------------------------------------------------------------

    public sealed class ScenarioABadKnobFailsBoot
    {
        // Given Gardener__NudgeGain=7, When the api boots — the Announcements/RequestsOptions
        // ValidateOnStart idiom: a top-level [Range] violation fails the WHOLE host at startup,
        // never a running station with a silently-clamped or ignored knob.
        [Fact]
        public void ItExitsNamingTheOffendingKey()
        {
            using var factory = new GardenerKnobsWebFactory(("NudgeGain", "7"));

            var ex = Assert.Throws<OptionsValidationException>(() => factory.Services);

            Assert.Contains("NudgeGain", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ItExitsNamingTheAllowedRange()
        {
            using var factory = new GardenerKnobsWebFactory(("NudgeGain", "7"));

            var ex = Assert.Throws<OptionsValidationException>(() => factory.Services);

            // DataAnnotations' RangeAttribute own verbatim message text (net10 default format) —
            // the "allowed range" AC6 asks for, checked exactly rather than a loose "contains a
            // digit" proxy that would pass for any range at all.
            Assert.Contains("must be between 0 and 2", ex.Message, StringComparison.Ordinal);
        }

        // T357 review HIGH-1: RangeAttribute(int, int) on a double property (the original
        // [Range(0, 2)]) converts an out-of-range fractional config value via Convert.ToInt32
        // (MidpointRounding.ToEven, i.e. banker's rounding) BEFORE comparing against the bound —
        // 2.4 rounds to 2 (in range) while the real, un-rounded double property is left at 2.4,
        // silently exceeding the documented ceiling and over-weighting the rotation nudge.
        // RangeAttribute(double, double) (GardenerOptions.NudgeGain's fixed attribute) compares the
        // double directly, with no conversion step, so this value genuinely fails boot.
        [Fact]
        public void AFractionalValueJustAboveTheCeilingFailsBoot()
        {
            using var factory = new GardenerKnobsWebFactory(("NudgeGain", "2.4"));

            Assert.Throws<OptionsValidationException>(() => factory.Services);
        }

        // The floor's own mirror of the same HIGH-1 conversion trap: Convert.ToInt32(-0.5) rounds
        // to 0 (banker's rounding, midpoint-to-even) — in range under the buggy int-based
        // RangeAttribute, even though a negative NudgeGain would invert the thumbs signal (Score +=
        // Nudge × NudgeGain flips sign) rather than merely under-weighting it.
        [Fact]
        public void ANegativeFractionalValueBelowTheFloorFailsBoot()
        {
            using var factory = new GardenerKnobsWebFactory(("NudgeGain", "-0.5"));

            Assert.Throws<OptionsValidationException>(() => factory.Services);
        }
    }
}
