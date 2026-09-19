// STORY-431 — Settings, options, laws, and the release (SPEC F176.1–F176.4 · PLAN T433/T453)
//
// BDD specification — xUnit. This file carries T433's own five ACs (AC1–AC4, AC7); AC5 (L10) is
// T443's, over in GenWave.Architecture.Tests. AC6 is T453's, implemented below.
//
// AC1's key-list parity is Story151_SeededDefaults.cs's own FeatureSettingsHelpKeysParity fact's
// job; this file's own facts instead pin the three things only THIS task's wording edit can break:
// the runtime 400 message, the FIELD_HELP_TEXT wording, and the TS mirror still carrying the key.
// AC2/AC3 drive the real GenWave.Ads.AdsServiceCollectionExtensions.AddGenWaveAds registration through a
// WebApplicationFactory<Program> boot — the Story380 GardenerKnobsWebFactory shape — so both facts
// prove production wiring, not a hand-built options chain. AC4 drives the real SettingsController
// the Story043 FeatureStationSettingsApi shape drives, in-process with fakes (no HTTP/DB needed:
// a non-allowlisted key is rejected before either the store or the DB is ever touched). AC7 reads
// DEPLOYMENT.md from the repo root the Story174_PublicTopologyDocs way. AC6 diffs the live-built
// GenWave.Abstractions assembly's public surface (GenWave.Host.Tests.Support.PublicSurface.Of)
// against the committed baseline for the package's current version — this release DID touch
// src/GenWave.Abstractions (SPEC F189.1), so AC6 proves the surface is exactly the regenerated
// one this same PR produced, and every release after this one proves the surface held steady
// since the last regeneration.

using System.Text.RegularExpressions;
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
using Microsoft.Extensions.Options;
using GenWave.Ads;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSponsorSettingsOptionsLawsAndTheRelease
{
    /// <summary>
    /// Repo root, resolved by walking up from the test assembly's build output until it finds
    /// <c>GenWave.sln</c> — the Story174_PublicTopologyDocs convention for reaching repo-root
    /// files from a test project. Shared by every fact in this file that reads a repo-root file,
    /// rather than each scenario growing its own copy.
    /// </summary>
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAntiRepeatWindowHelpTextChanged
    {
        static string SettingsFormTsxPath() =>
            Path.Combine(RepoRoot(), "admin-ui", "app", "(authed)", "settings", "SettingsForm.tsx");

        static string SettingsHelpKeysTsPath() =>
            Path.Combine(RepoRoot(), "admin-ui", "app", "(authed)", "settings", "settings-help-keys.ts");

        /// <summary>
        /// Extracts the (possibly multi-line, string-concatenated) <c>FIELD_HELP_TEXT</c> value for
        /// <c>"Station:Ads:AntiRepeatWindow"</c> — bounded from that key's own marker to the next
        /// dictionary entry (a line starting with two spaces and a quote, the Story151
        /// bounded-region idiom), so this makes no assumption about which key comes next.
        /// </summary>
        static string AntiRepeatWindowHelpText()
        {
            var path = SettingsFormTsxPath();
            var text = File.ReadAllText(path);

            const string marker = "\"Station:Ads:AntiRepeatWindow\":";
            var start = text.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"could not find '{marker}' in {path}");
            var valueStart = start + marker.Length;

            var nextEntry = Regex.Match(text[valueStart..], "\n  \"");
            Assert.True(nextEntry.Success, $"could not find the next FIELD_HELP_TEXT entry after '{marker}' in {path}");
            var valueText = text[valueStart..(valueStart + nextEntry.Index)];

            return string.Concat(Regex.Matches(valueText, "\"([^\"]*)\"").Select(m => m.Groups[1].Value));
        }

        [Fact]
        public void TheHelpTextSaysSponsor()
        {
            var helpText = AntiRepeatWindowHelpText();

            // F173.3: the unit becomes sponsors — the wording (previously "spots"/"ad") names the
            // sponsor, not the individual spot.
            Assert.Contains("sponsor", helpText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("spot", helpText, StringComparison.OrdinalIgnoreCase);

            Assert.True(
                StationSettingsAllowlist.ByKey.TryGetValue("Station:Ads:AntiRepeatWindow", out var setting),
                "\"Station:Ads:AntiRepeatWindow\" is not on the settings allowlist.");
            Assert.Equal("sponsors", setting.Unit);
        }

        [Fact]
        public void TheOutOfRangeErrorMessageSaysSponsor()
        {
            var validator = new SettingValidator(new ConfigurationBuilder().Build());

            var message = validator.Validate("Station:Ads:AntiRepeatWindow", "99");

            // F173.3: the 400 the runtime API guard returns for an out-of-range value must use the
            // same wording as the Unit/help-text surfaces above, not the pre-rename "spots".
            Assert.NotNull(message);
            Assert.Contains("sponsors", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("spots", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TheF56HelpKeyParitySpecStillPasses()
        {
            // Claim 1 (F173.3): the FIELD_HELP_TEXT entry itself no longer names the unit "spot" —
            // this is the literal wording edit T433 makes, so this claim (not a re-run of
            // Story151's key-LIST parity fact, which is wording-blind) is the one that goes red if
            // the rename is reverted.
            var helpText = AntiRepeatWindowHelpText();
            Assert.DoesNotContain("spot", helpText, StringComparison.OrdinalIgnoreCase);

            // Claim 2 (F176.1): the key survives the wording edit on the TS-mirror side too —
            // still allowlisted (Story151's own fact) AND still helped (this claim), never one
            // without the other.
            var tsMirrorText = File.ReadAllText(SettingsHelpKeysTsPath());
            Assert.Contains("\"Station:Ads:AntiRepeatWindow\"", tsMirrorText, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioEnvKnobsBind
    {
        // Given Ads:PreviewRetentionDays=14 supplied via AdsKnobsWebFactory's colon-form UseSetting
        // mirror of Ads__PreviewRetentionDays (JobQueueCapacity left unset), When the api boots.
        [Fact]
        public void PreviewRetentionDaysBindsFromEnv()
        {
            using var factory = new AdsKnobsWebFactory(("PreviewRetentionDays", "14"));

            var options = factory.Services.GetRequiredService<IOptions<AdsOptions>>().Value;

            Assert.Equal(14, options.PreviewRetentionDays);
            Assert.Equal(8, options.JobQueueCapacity); // the documented default, genuinely unset
        }

        // Given Ads:JobQueueCapacity=16 supplied via AdsKnobsWebFactory's colon-form UseSetting
        // mirror of Ads__JobQueueCapacity (PreviewRetentionDays left unset), When the api boots.
        [Fact]
        public void JobQueueCapacityBindsFromEnv()
        {
            using var factory = new AdsKnobsWebFactory(("JobQueueCapacity", "16"));

            var options = factory.Services.GetRequiredService<IOptions<AdsOptions>>().Value;

            Assert.Equal(16, options.JobQueueCapacity);
            Assert.Equal(7, options.PreviewRetentionDays); // the documented default, genuinely unset
        }
    }

    public sealed class ScenarioDeploymentListsTheTwoKnobsExactlyOnce
    {
        static string DeploymentMdText()
        {
            var path = Path.Combine(RepoRoot(), "DEPLOYMENT.md");
            Assert.True(File.Exists(path), "DEPLOYMENT.md is missing from the repo root.");
            return File.ReadAllText(path);
        }

        static int CountOccurrences(string haystack, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        /// <summary>
        /// Counts both the compose-file spelling (<c>Ads__X</c>) and the appsettings/colon
        /// spelling (<c>Ads:X</c>) together — DEPLOYMENT.md must document each knob exactly once,
        /// regardless of which spelling that one mention uses.
        /// </summary>
        static int CountEitherSpelling(string haystack, string dunderForm, string colonForm) =>
            CountOccurrences(haystack, dunderForm) + CountOccurrences(haystack, colonForm);

        [Fact]
        public void PreviewRetentionDaysAppearsOnce() =>
            Assert.Equal(
                1,
                CountEitherSpelling(DeploymentMdText(), "Ads__PreviewRetentionDays", "Ads:PreviewRetentionDays"));

        [Fact]
        public void JobQueueCapacityAppearsOnce() =>
            Assert.Equal(
                1,
                CountEitherSpelling(DeploymentMdText(), "Ads__JobQueueCapacity", "Ads:JobQueueCapacity"));
    }

    public sealed class ScenarioAbstractionsUnchanged
    {
        static string BaselinePath() =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "abstractions-5.10.0-surface.txt");

        /// <summary>
        /// The committed baseline's own lines, minus its first-line provenance comment (see
        /// <c>Fixtures/abstractions-5.10.0-surface.txt</c>'s own header).
        /// </summary>
        static IReadOnlyList<string> BaselineLines()
        {
            var path = BaselinePath();
            Assert.True(File.Exists(path), $"missing baseline fixture at {path} — is it CopyToOutputDirectory in the csproj?");
            return File.ReadAllLines(path).Skip(1).ToList();
        }

        // AC6 (SPEC F176.4): the package public surface must stay byte-identical between
        // regenerations — so a release that leaves src/GenWave.Abstractions alone ships the surface
        // it shipped last time, and one that touches it may move this baseline only by regenerating
        // it. PublicSurface.Of enumerates the LIVE-built Abstractions assembly (any exported type
        // reaches the same assembly, so IAdSpotSource is just a convenient anchor) the same way the
        // baseline was generated — a real Abstractions edit changes what this Fact reads, not a
        // hand-maintained expectation. PLAN T453 ruling: a legitimate Abstractions bump regenerates
        // Fixtures/abstractions-<version>-surface.txt in the same PR that makes the bump, rather
        // than this Fact ever being hand-edited to tolerate a diff — and a regeneration is itself
        // diffed against the previous baseline by hand, removals accounted for at zero, because
        // this Fact cannot fail at the commit that regenerates it (Fixtures/README.md states both
        // permitted provenances and that check).
        //
        // PLAN T530 closed the one tolerance window this law has ever needed: SPEC F189.1's
        // additive contract change (gh-#772) landed in src/GenWave.Abstractions across T524–T529,
        // ahead of the package bump, so PLAN T524 round-2 named a temporary, line-by-line allowlist
        // for exactly those additions. T530 is the version-bump PR itself — it regenerates this
        // baseline from the release commit's own Release build of src/GenWave.Abstractions (the
        // published 5.10.0 nupkg does not exist yet; it publishes on the v5.10.0 tag, PLAN T531,
        // which cuts this same tree) and deletes the allowlist in the same PR, so the fact below
        // again compares directly with no third tolerance list.
        [Fact]
        public void ThePackageSurfaceDiffVs5100IsEmpty()
        {
            var baseline = BaselineLines();
            Assert.True(baseline.Count > 20, $"the baseline fixture has only {baseline.Count} lines — too small to be a real enumeration.");

            var current = PublicSurface.Of(typeof(IAdSpotSource).Assembly);

            var added = ExcessOf(current, baseline);
            var removed = ExcessOf(baseline, current);

            if (added.Count == 0 && removed.Count == 0)
                return;

            var message = string.Join(
                Environment.NewLine,
                new[] { "GenWave.Abstractions public surface differs from the 5.10.0 baseline:" }
                    .Concat(added.Select(l => $"+ {l}"))
                    .Concat(removed.Select(l => $"- {l}")));
            Assert.Fail(message);
        }

        /// <summary>
        /// The lines present in <paramref name="from"/> more times than in
        /// <paramref name="than"/> — a count-aware (multiset) difference, not a set difference, so
        /// a change in how many times an identical line occurs (e.g. an overload that would
        /// otherwise render the same line twice) can never be masked by de-duplication. Ordinally
        /// sorted, one entry per excess occurrence.
        /// </summary>
        static List<string> ExcessOf(IReadOnlyList<string> from, IReadOnlyList<string> than)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var line in than)
                counts[line] = counts.GetValueOrDefault(line) + 1;

            var excess = new List<string>();
            foreach (var line in from)
            {
                var remaining = counts.GetValueOrDefault(line);
                if (remaining > 0)
                    counts[line] = remaining - 1;
                else
                    excess.Add(line);
            }

            excess.Sort(StringComparer.Ordinal);
            return excess;
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingBadValues
    {
        // Given Ads:PreviewRetentionDays=0 supplied via AdsKnobsWebFactory's colon-form UseSetting
        // mirror of Ads__PreviewRetentionDays (below the 1..90 range), When the api boots.
        [Fact]
        public void PreviewRetentionDaysZeroFailsStartupNamingTheProperty()
        {
            using var factory = new AdsKnobsWebFactory(("PreviewRetentionDays", "0"));

            var ex = Assert.Throws<OptionsValidationException>(() => factory.Services);

            Assert.Contains("PreviewRetentionDays", ex.Message, StringComparison.Ordinal);
        }

        // Given a Live settings PUT for Ads:PreviewRetentionDays / Ads:JobQueueCapacity, When the
        // request runs — both are env/compose-only (F176.2), never allowlisted, so the SAME
        // rejection path Story043's NonAllowlistedKeyIsRejectedWith400 exercises applies to both.
        [Fact]
        public async Task ALiveSettingsPutForAnEnvKnobIs400()
        {
            var config = new ConfigurationBuilder().Build();
            var controller = new SettingsController(
                config,
                new UnreachableSettingsStore(),
                new SettingValidator(config),
                NullLogger<SettingsController>.Instance,
                new FakeIconPackStore())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

            foreach (var key in new[] { "Ads:PreviewRetentionDays", "Ads:JobQueueCapacity" })
            {
                Assert.False(
                    StationSettingsAllowlist.ByKey.ContainsKey(key),
                    $"\"{key}\" must stay env/compose-only (F176.2) — it must never be allowlisted.");

                var result = await controller.Put(
                    new List<SettingUpdateRequest> { new(key, "14") }, CancellationToken.None);

                Assert.IsType<BadRequestObjectResult>(result);
            }
        }

        /// <summary>
        /// <see cref="IStationSettingsStore"/> double whose <see cref="WriteAsync"/> throws —
        /// SettingsController.Put rejects a non-allowlisted key during its own validation pass,
        /// before the store is ever touched (mirrors Story043's WriteCallCount==0 assertion, but as
        /// a hard failure rather than a counter, since this fact drives the real controller with no
        /// other store call on its path).
        /// </summary>
        sealed class UnreachableSettingsStore : IStationSettingsStore
        {
            public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("WriteAsync must not be reached for a non-allowlisted key.");

            public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        }
    }
}

// ── T433's own AC2/AC3 harness ─────────────────────────────────────────────────────────────────

/// <summary>
/// Boots the real production composition root (Program.cs) with a fake, unreachable
/// <c>ConnectionStrings:Library</c>, mirroring Story380's <c>GardenerKnobsWebFactory</c>: every
/// option class Program.cs binds (including <see cref="AdsOptions"/>) is genuinely wired and
/// boot-validated, but neither AC this file drives ever queries Postgres, so a real ephemeral
/// database is pure overhead here. <paramref name="adsOverrides"/> mirrors <c>Ads__*</c> env vars
/// as colon-form <c>UseSetting</c> keys — a per-instance value with no shared process state; the
/// <c>__</c>→<c>:</c> mapping is the framework's env provider, not what these facts prove.
/// </summary>
file sealed class AdsKnobsWebFactory(params (string Key, string Value)[] adsOverrides)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story431-ads-knobs";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint so
        // ValidateOnStart() is satisfied without injecting them manually (the Story084/Story125/
        // Story380 precedent).
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        foreach (var (key, value) in adsOverrides)
            builder.UseSetting($"Ads:{key}", value);

        builder.ConfigureTestServices(services =>
        {
            // No AdsLibrarySeedHostedService/AdSpotWorker/AdSpotLifecycleGuardianService (or any
            // other DB-touching background loop) during this test — mirrors GardenerKnobsWebFactory.
            services.RemoveAll<IHostedService>();
        });
    }
}
