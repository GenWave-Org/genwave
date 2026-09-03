// STORY-394 — Ship it honest: posture, laws, settings (F157.3/.4, F163 · PLAN T406)
// AC4 (release + demo) is manual (Dean) — no spec; it closes on his word (T408/T409).
using GenWave.Architecture.Tests.Support;
using GenWave.Host.Configuration;
using Microsoft.Extensions.Configuration;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureShipHonestPins
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePostureIsWrittenDown
    {
        [Fact]
        public void PluginsMdExistsAtTheRepoRoot()
        {
            // The F157.3 statement: MIT to compile, AGPL-compatible to distribute in-proc,
            //   unconstrained out-of-proc, no obligation for private plugins — pinned by real
            //   content (not merely the file's existence), so a future edit that drops a clause
            //   goes red here, not just at review.
            var path = Path.Combine(SolutionLocator.Root(), "PLUGINS.md");
            Assert.True(File.Exists(path), "PLUGINS.md does not exist at the repo root.");

            var text = File.ReadAllText(path);

            Assert.Contains("Compiling against `GenWave.Abstractions`", text, StringComparison.Ordinal);
            Assert.Contains("MIT-clean for anyone", text, StringComparison.Ordinal);
            Assert.Contains("must carry an AGPL-compatible", text, StringComparison.Ordinal);
            Assert.Contains("A private, undistributed plugin carries no obligation", text, StringComparison.Ordinal);
            Assert.Contains("Out-of-process integrations are unconstrained", text, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioTheLawsKnowTheNewProjects
    {
        [Fact]
        public void L5SeedsIncludeGenWaveHostPlugins()
        {
            // HostReservedNamespaces gains the row; the loader is born outside Host (F157.4).
            var entry = Assert.Single(HostReservedNamespaces.Entries, e => e.ReservedNamespace == "GenWave.Host.Plugins");
            Assert.Equal("F157.4", entry.RulingReference);

            // And Host itself actually honors the reservation — the plugin-door wiring (PLAN T394)
            // must never land its own logic under that namespace (mirrors
            // Story292_HostTripwire.ScenarioTheMechanismAndTheSeed.TodaysHostPassesWithTheSeededReservations,
            // re-run here with the grown seed so a future PR that lands plugin-door glue in
            // GenWave.Host.Plugins fails THIS fact, not just the general one).
            var violations = HostNamespaceTripwire.FindViolations(
                ProductionAssemblies.Host.GetTypes(), HostReservedNamespaces.Entries);

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
        }

        [Fact]
        public void L10RootsIncludePluginsAndAds()
        {
            // The cycle-freedom TheoryData gains GenWave.Plugins and GenWave.Ads (STORY-394 AC2).
            // HONEST FRAMING (PLAN T406 review MED-5 — the prior wording here overclaimed): this is a
            // FORWARD-LOOKING addition, not a retroactive proof of anything cyclic today. Both
            // projects' OWN namespace tree is FLAT right now — every "GenWave.Plugins*"/
            // "GenWave.Ads*"-prefixed type resolves to exactly the bare root, no sub-namespace segment
            // beneath it, verified against the real assemblies below — so NamespaceCycleFence's own
            // slice match ("GenWave.Ads.(*)") finds ZERO slices for either root and the check below
            // passes VACUOUSLY, the exact "guards wherever internal structure exists" posture
            // FeatureNamespaceCycleFreedom's own remarks already document for GenWave.Tts. (A
            // source-generated regex helper type DOES live in this assembly under its own unrelated
            // "System.Text.RegularExpressions.Generated" namespace — found empirically writing this
            // fact — but that namespace shares no "GenWave.Plugins" prefix, so it is invisible to the
            // slice match and irrelevant to this project's own flatness; the filter below excludes it
            // on purpose, not by accident.) What the TheoryData addition buys: the moment either
            // project grows its FIRST internal sub-namespace, a cycle among that project's own slices
            // goes red through this same detector, with no further wiring ever needed — L10 is
            // watching from here on.
            //
            // What this fence does NOT check, and never has (STORY-390's own Ads->Tts reference,
            // AdScriptWriter's ProjectReference, included): an inter-PROJECT edge. A project-level
            // cycle is a compile error MSBuild refuses outright (a circular ProjectReference cannot
            // exist), so L10's whole reason to exist — per Gh445_NamespaceCycleFreedom.cs's own
            // remarks — is the tangle THAT structural guarantee can't reach: cycles among namespaces
            // INSIDE one assembly. Confusing the two here would be exactly PLAN T406's review finding.
            var pluginsNamespaces = ProductionAssemblies.Plugins.GetTypes()
                .Select(t => t.Namespace)
                .Where(ns => ns is not null && ns.StartsWith("GenWave.Plugins", StringComparison.Ordinal))
                .Distinct();
            var adsNamespaces = ProductionAssemblies.Ads.GetTypes()
                .Select(t => t.Namespace)
                .Where(ns => ns is not null && ns.StartsWith("GenWave.Ads", StringComparison.Ordinal))
                .Distinct();
            Assert.Equal(new[] { "GenWave.Plugins" }, pluginsNamespaces);
            Assert.Equal(new[] { "GenWave.Ads" }, adsNamespaces);

            IEnumerable<string> roots = FeatureNamespaceCycleFreedom.ProjectRoots;
            Assert.Contains("GenWave.Plugins", roots);
            Assert.Contains("GenWave.Ads", roots);

            // The detector genuinely runs against the real production graph for both roots (vacuous
            // today per the flat-namespace fact above, not merely listed in the TheoryData) — proves
            // the wiring reaches NamespaceCycleFence.FindViolations, not that a cycle was found.
            // Gh445_NamespaceCycleFreedom.cs's own ScenarioASyntheticNamespaceCycleIsRed is what
            // proves this detector can find a REAL cycle once one exists.
            foreach (var root in new[] { "GenWave.Plugins", "GenWave.Ads" })
            {
                var violations = NamespaceCycleFence.FindViolations(root, ProductionArchitecture.Instance);
                DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
            }
        }

        [Fact]
        public void TheAddHttpClientPinStillReadsThree()
        {
            // Neither the loader nor the ads lane owns HTTP (F157.4/F160.1) — the same detector
            // Story291_ConventionLaws.ScenarioL3ProgramCompositionRoot calls (ProgramHttpClientRegistrations,
            // Support/ — PLAN T406 review MED-4: one regex literal, one place), re-asserted here as
            // this story's own independent pin on the epic's net effect.
            var programText = ProgramHttpClientRegistrations.ReadProgramText();

            var registrationCount = ProgramHttpClientRegistrations.CountAddHttpClientRegistrations(programText);

            Assert.Equal(3, registrationCount);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the settings split cannot drift
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheSettingsSplitHolds
    {
        [Fact]
        public void TheFiveStationAdsKeysAreLiveWithValidatorsAndHelpText()
        {
            // EveryNUnits/TargetCount/RefreshDays/AutoApprove/AntiRepeatWindow — allowlisted,
            //   validated, three-way help parity (F163.1). Help-text coverage/parity is guarded by
            //   the admin-ui jest suite (settings-help-coverage.spec.tsx) — this fact pins the two
            //   halves a C# suite can actually see: the allowlist row shape and the validator's
            //   enforced range.
            var expected = new (string Key, SettingKind Kind)[]
            {
                ("Station:Ads:EveryNUnits", SettingKind.Number),
                ("Station:Ads:TargetCount", SettingKind.Number),
                ("Station:Ads:RefreshDays", SettingKind.Number),
                ("Station:Ads:AutoApprove", SettingKind.Boolean),
                ("Station:Ads:AntiRepeatWindow", SettingKind.Number),
            };

            foreach (var (key, kind) in expected)
            {
                Assert.True(
                    StationSettingsAllowlist.ByKey.TryGetValue(key, out var setting),
                    $"\"{key}\" is not on the settings allowlist.");
                Assert.Equal(SettingApplyMode.Live, setting.ApplyMode);
                Assert.Equal(kind, setting.Kind);
            }

            // The validator enforces a real numeric range on every non-boolean row — the ceiling
            // itself passes, one past it fails (SettingValidator's own AdsEveryNUnitsMin/Max etc.
            // are `internal` with no InternalsVisibleTo grant into this project, so the bounds are
            // re-asserted here as bare numbers, matched against SettingValidator.cs's own comment —
            // change one, change the other).
            var validator = new SettingValidator(new ConfigurationBuilder().Build());

            AssertRangeEnforced(validator, "Station:Ads:EveryNUnits", min: 0, max: 1000);
            AssertRangeEnforced(validator, "Station:Ads:TargetCount", min: 0, max: 100);
            AssertRangeEnforced(validator, "Station:Ads:RefreshDays", min: 1, max: 365);
            AssertRangeEnforced(validator, "Station:Ads:AntiRepeatWindow", min: 0, max: 50);

            Assert.Null(validator.Validate("Station:Ads:AutoApprove", "true"));
            Assert.NotNull(validator.Validate("Station:Ads:AutoApprove", "not-a-bool"));
        }

        static void AssertRangeEnforced(SettingValidator validator, string key, int min, int max)
        {
            Assert.Null(validator.Validate(key, min.ToString()));
            Assert.Null(validator.Validate(key, max.ToString()));
            Assert.NotNull(validator.Validate(key, (min - 1).ToString()));
            Assert.NotNull(validator.Validate(key, (max + 1).ToString()));
        }

        [Fact]
        public void NoPluginsOrAdsInfraKeyIsAllowlisted()
        {
            // Plugins:* and Ads:* never appear in StationSettingsAllowlist (F156.1/F163.2) —
            // Plugins:{name}:* is env/compose-only (IPluginHost.Setting reads IConfiguration
            // directly, F157.2); GenWave.Ads.AdsOptions' own Ads:* section is the identical
            // env/compose-only posture. Only Station:Ads:* (five rows, asserted above) is Live.
            foreach (var setting in StationSettingsAllowlist.All)
            {
                Assert.False(
                    setting.Key.StartsWith("Plugins:", StringComparison.OrdinalIgnoreCase),
                    $"\"{setting.Key}\" is a plugin infra key — Plugins:{{name}}:* must stay " +
                    "env/compose-only, never allowlisted (F157.2).");

                Assert.False(
                    setting.Key.StartsWith("Ads:", StringComparison.OrdinalIgnoreCase),
                    $"\"{setting.Key}\" is an Ads infra key — GenWave.Ads.AdsOptions' Ads:* section " +
                    "must stay env/compose-only, never allowlisted (F163.2); only Station:Ads:* is Live.");
            }
        }
    }
}
