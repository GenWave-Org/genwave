// STORY-290 — The dependency laws run red-on-violation (SPEC F105.1, F105.2 · PLAN T211)
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using GenWave.Architecture.Tests.Support;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// Feature: the dependency-direction laws as fitness tests. L1 (framework-free inner
/// projects), L2 (Postgres confinement), and L4's reference half (Abstractions = BCL-only)
/// run inside the normal <c>dotnet test</c> gate. Adopted at T211: the analysis library
/// (ArchUnitNET, see the csproj comment) plus the named/dated exemption mechanism every later
/// law (T212–T214) reuses.
/// </summary>
public sealed class FeatureDependencyLaws
{
    public sealed class ScenarioTheSuiteRunsInTheNormalGate
    {
        [Fact]
        public void TheArchitectureSuiteExecutesAlongsideTheFiveTestProjects()
        {
            var solutionText = File.ReadAllText(SolutionLocator.Find());

            var testProjects = new[]
            {
                "GenWave.Architecture.Tests",
                "GenWave.Core.Tests",
                "GenWave.Host.Tests",
                "GenWave.MediaLibrary.Tests",
                "GenWave.Orchestration.Tests",
                "GenWave.Tts.Tests",
            };

            foreach (var project in testProjects)
                Assert.Contains($"\"{project}\"", solutionText);

            // Functional half — not just declared in the sln, but actually able to load and
            // inspect the real production graph in this same run (no separate CI lane).
            Assert.NotEmpty(ProductionArchitecture.Instance.Classes);
        }
    }

    public sealed class ScenarioL1FrameworkFreeInnerProjects
    {
        [Fact]
        public void CoreOrchestrationTtsAndLoudnessReferenceNoAspNetNpgsqlOrDapper()
        {
            // AssemblyReferenceScan.HasFamilyPrefix is segment-boundary aware (its own remarks:
            // the same shape of hole L4-references' name-prefix check had — DepsJsonDependencyScan's
            // remarks). Lifted there at T212 (T211 review carry-forward) so it's a tested member
            // with its own synthetic-string probe facts instead of an uncovered local function.
            static bool IsForbidden(string referencedAssemblyName) =>
                AssemblyReferenceScan.HasFamilyPrefix(referencedAssemblyName, "Microsoft.AspNetCore")
                || referencedAssemblyName is "Npgsql" or "Dapper";

            var violations = ProductionAssemblies.InnerProjects
                .SelectMany(project => AssemblyReferenceScan
                    .ForbiddenReferences(project.Assembly.Location, IsForbidden)
                    .Select(reference => new LawViolation(
                        LawId.L1, project.Label, $"references forbidden assembly \"{reference}\"")))
                .ToList();

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
        }
    }

    public sealed class ScenarioL2PostgresConfinement
    {
        [Fact]
        public void NpgsqlAndDapperAppearOnlyInTheRepositoryLayer()
        {
            var assemblies = ProductionAssemblies.AllProductionAssemblies();
            var genwaveAssemblies = Types().That().ResideInAssembly(assemblies[0]);
            for (var i = 1; i < assemblies.Count; i++)
                genwaveAssemblies = genwaveAssemblies.Or().ResideInAssembly(assemblies[i]);

            var subjects = Types().That().Are(genwaveAssemblies).And().AreNot(PostgresConfinement.RepositoryLayer);

            var violations = PostgresConfinement.FindViolations(ProductionArchitecture.Instance, subjects);

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
        }

        [Fact]
        public void TheCompositionRootsDataSourceConstructionIsTheOneNamedExemption()
        {
            var compositionRootExemptions = ExemptionBaseline.Entries
                .Where(e => e.LawId == LawId.L2
                    && e.Member.StartsWith("GenWave.Host.", StringComparison.Ordinal)
                    && e.Reason.Contains("composition root", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var exemption = Assert.Single(compositionRootExemptions);
            Assert.Equal(
                "GenWave.Host.Seeding.PersonaCardMigrationServiceCollectionExtensions", exemption.Member);

            // And it earns the exemption honestly: re-running the raw (pre-baseline) detector
            // against just this one type shows it depends on NpgsqlDataSource/NpgsqlDataSourceBuilder
            // construction — never NpgsqlConnection/Command/DataReader, which would mean it queries.
            var subjects = Types().That().HaveFullName(exemption.Member);
            var violations = PostgresConfinement.FindViolations(ProductionArchitecture.Instance, subjects);
            var violation = Assert.Single(violations);

            Assert.Contains("NpgsqlDataSource", violation.Detail);
            Assert.DoesNotContain("NpgsqlConnection", violation.Detail);
            Assert.DoesNotContain("NpgsqlCommand", violation.Detail);
            Assert.DoesNotContain("NpgsqlDataReader", violation.Detail);
        }
    }

    public sealed class ScenarioL4ReferenceHygiene
    {
        [Fact]
        public void AbstractionsReferencesNothingBeyondTheBcl()
        {
            var extraLibraries = DepsJsonDependencyScan.ExtraLibrariesForProject(
                "src/GenWave.Abstractions", "GenWave.Abstractions");

            var violations = extraLibraries
                .Select(library => new LawViolation(
                    LawId.L4References, "GenWave.Abstractions", $"depends on non-BCL package \"{library}\""))
                .ToList();

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
        }
    }

    public sealed class ScenarioViolationsAreRedAndNamed
    {
        // ── L2 (ArchUnitNET-based detector) ─────────────────────────────────────────────────────
        private readonly IReadOnlyList<LawViolation> l2Violations;
        private readonly LawViolation l2Violation;
        private readonly string l2Message;

        // ── L1 (hand-rolled AssemblyReferenceScan) ──────────────────────────────────────────────
        private readonly IReadOnlyList<string> l1MatchedOnAKnownReference;
        private readonly IReadOnlyList<string> l1MatchedOnANameItNeverReferences;
        private readonly string l1Message;

        // ── L1's HasFamilyPrefix (T211 review carry-forward, lifted at T212) ────────────────────
        private readonly bool hasFamilyPrefixForBareFamilyName;
        private readonly bool hasFamilyPrefixForDottedFamilyMember;
        private readonly bool hasFamilyPrefixForSamePrefixLookalike;

        // ── L4-references (deps.json-based DepsJsonDependencyScan) ─────────────────────────────
        private readonly IReadOnlyList<string> l4ExtraWhenSelfOnly;
        private readonly IReadOnlyList<string> l4ExtraWhenPolluted;
        private readonly string l4Message;

        public ScenarioViolationsAreRedAndNamed()
        {
            // Reuses PostgresConfinement.FindViolations — the exact function
            // NpgsqlAndDapperAppearOnlyInTheRepositoryLayer runs above — scoped to a fixture
            // namespace instead of GenWave.MediaLibrary's, so this proof needs no production edit
            // and stays stable regardless of the codebase's current violation count.
            var fixtureArchitecture = new ArchLoader()
                .LoadAssemblies(
                    typeof(Fixtures.L2Probe.RepositoryLike.CompliantRepository).Assembly,
                    ProductionAssemblies.Npgsql,
                    ProductionAssemblies.Dapper)
                .Build();

            // Two independent chains, not one derived from the other: ArchUnitNET's fluent
            // builders share mutable state across a chain, so extending a captured step (e.g.
            // `repositoryLike.Or()...`) would silently widen `repositoryLike` itself too.
            var repositoryLike = Types().That()
                .ResideInNamespace("GenWave.Architecture.Tests.Fixtures.L2Probe.RepositoryLike");
            var probeTypes = Types().That()
                .ResideInNamespace("GenWave.Architecture.Tests.Fixtures.L2Probe.RepositoryLike")
                .Or().ResideInNamespace("GenWave.Architecture.Tests.Fixtures.L2Probe.Elsewhere");
            var subjects = Types().That().Are(probeTypes).And().AreNot(repositoryLike);

            l2Violations = PostgresConfinement.FindViolations(fixtureArchitecture, subjects);
            l2Violation = l2Violations[0];
            l2Message = DependencyLawAssert.Format(l2Violation);

            // L1's probe target: ArchUnitNET.dll, a stable third-party binary this project already
            // PackageReferences directly — not GenWave's own code, so this proof stays decoupled
            // from GenWave's live dependency graph. Verified (T211 review): it references exactly
            // netstandard, Mono.Cecil, StronglyConnectedComponents, System.Collections.Immutable,
            // Mono.Cecil.Rocks, Newtonsoft.Json — Mono.Cecil is always present (kills an
            // "always-empty" detector), "Microsoft.AspNetCore" is never present (kills an
            // "always-flag" detector).
            var l1ProbeAssembly = typeof(ArchUnitNET.Domain.Architecture).Assembly.Location;
            l1MatchedOnAKnownReference = AssemblyReferenceScan.ForbiddenReferences(
                l1ProbeAssembly, name => name == "Mono.Cecil");
            l1MatchedOnANameItNeverReferences = AssemblyReferenceScan.ForbiddenReferences(
                l1ProbeAssembly, name => name == "Microsoft.AspNetCore");
            var l1Violation = new LawViolation(
                LawId.L1, "Probe.ArchUnitNET", $"references forbidden assembly \"{l1MatchedOnAKnownReference[0]}\"");
            l1Message = DependencyLawAssert.Format(l1Violation);

            // HasFamilyPrefix's own probe: synthetic strings, no assembly read at all (the L4
            // fixture precedent below) — proves the segment-boundary discrimination the T211 review
            // flagged as uncovered. A bare family name and a dotted family member must both match; a
            // same-prefix lookalike (the exact hole a bare StartsWith would miss) must not.
            hasFamilyPrefixForBareFamilyName =
                AssemblyReferenceScan.HasFamilyPrefix("Microsoft.AspNetCore", "Microsoft.AspNetCore");
            hasFamilyPrefixForDottedFamilyMember =
                AssemblyReferenceScan.HasFamilyPrefix("Microsoft.AspNetCore.Http", "Microsoft.AspNetCore");
            hasFamilyPrefixForSamePrefixLookalike =
                AssemblyReferenceScan.HasFamilyPrefix("Microsoft.AspNetCoreLike", "Microsoft.AspNetCore");

            // L4-references' probe target: synthetic deps.json content (Fixtures/L4Probe), never
            // read from disk — decoupled from GenWave.Abstractions' real, live dependency graph. A
            // self-only closure kills an "always-flag" detector; a self-plus-one-package closure
            // (shaped exactly like the System.Diagnostics.EventLog bypass) kills an "always-empty"
            // one.
            l4ExtraWhenSelfOnly = DepsJsonDependencyScan.ExtraLibraries(
                Fixtures.L4Probe.DepsJsonFixtures.SelfOnly, "Probe.Assembly");
            l4ExtraWhenPolluted = DepsJsonDependencyScan.ExtraLibraries(
                Fixtures.L4Probe.DepsJsonFixtures.SelfPlusExtraPackage, "Probe.Assembly");
            var l4Violation = new LawViolation(
                LawId.L4References, "Probe.Assembly", $"depends on non-BCL package \"{l4ExtraWhenPolluted[0]}\"");
            l4Message = DependencyLawAssert.Format(l4Violation);
        }

        [Fact]
        public void TheL2ProbeFindsExactlyOneViolation() => Assert.Single(l2Violations);

        [Fact]
        public void TheL2ViolationIsTaggedWithLawL2() => Assert.Equal(LawId.L2, l2Violation.LawId);

        [Fact]
        public void TheL2ViolationNamesTheOffendingType() => Assert.Equal(
            "GenWave.Architecture.Tests.Fixtures.L2Probe.Elsewhere.ViolatesConfinement", l2Violation.Member);

        [Fact]
        public void TheL2ViolationDetailNamesNpgsql() => Assert.Contains("Npgsql", l2Violation.Detail);

        [Fact]
        public void TheFormattedL2MessageNamesTheLaw() => Assert.Contains(LawId.L2, l2Message);

        [Fact]
        public void TheFormattedL2MessageNamesTheOffendingType() => Assert.Contains("ViolatesConfinement", l2Message);

        // The confined "repository-like" fixture is never evaluated (excluded from subjects,
        // exactly as MediaLibrary.Catalog/Station are) and the clean "elsewhere" fixture passes —
        // the rule doesn't fail indiscriminately.
        [Fact]
        public void TheCompliantRepositoryFixtureIsNeverFlagged() =>
            Assert.DoesNotContain(l2Violations, v => v.Member.Contains("CompliantRepository"));

        [Fact]
        public void TheCleanElsewhereFixtureIsNotFlagged() =>
            Assert.DoesNotContain(l2Violations, v => v.Member.Contains("StaysClean"));

        [Fact]
        public void TheL1ProbeFindsAKnownReference() =>
            Assert.Equal(new[] { "Mono.Cecil" }, l1MatchedOnAKnownReference);

        [Fact]
        public void TheL1ProbeFindsNothingForANameItNeverReferences() =>
            Assert.Empty(l1MatchedOnANameItNeverReferences);

        [Fact]
        public void TheFormattedL1MessageNamesTheLawAndTheAssembly() => Assert.Equal(
            "[L1] Probe.ArchUnitNET: references forbidden assembly \"Mono.Cecil\"", l1Message);

        [Fact]
        public void HasFamilyPrefixMatchesTheBareFamilyName() => Assert.True(hasFamilyPrefixForBareFamilyName);

        [Fact]
        public void HasFamilyPrefixMatchesADottedFamilyMember() => Assert.True(hasFamilyPrefixForDottedFamilyMember);

        [Fact]
        public void HasFamilyPrefixDoesNotMatchASamePrefixLookalike() =>
            Assert.False(hasFamilyPrefixForSamePrefixLookalike);

        [Fact]
        public void TheL4ProbeFindsNoExtraLibrariesWhenTheClosureIsSelfOnly() => Assert.Empty(l4ExtraWhenSelfOnly);

        [Fact]
        public void TheL4ProbeNamesTheExtraLibraryWhenTheClosureHasOneMore() => Assert.Equal(
            new[] { "System.Diagnostics.EventLog/9.0.0" }, l4ExtraWhenPolluted);

        [Fact]
        public void TheFormattedL4MessageNamesTheLawAndTheAssembly() => Assert.Equal(
            "[L4-references] Probe.Assembly: depends on non-BCL package \"System.Diagnostics.EventLog/9.0.0\"",
            l4Message);
    }

    public sealed class ScenarioExemptionsAreNamedDatedAndFailOnNew
    {
        [Fact]
        public void ABaselinedViolationIsNamedAndDatedInTheTestItself()
        {
            Assert.NotEmpty(ExemptionBaseline.Entries);

            var knownLawIds = new[] { LawId.L1, LawId.L2, LawId.L4References };

            foreach (var entry in ExemptionBaseline.Entries)
            {
                Assert.Contains(entry.LawId, knownLawIds);
                Assert.False(string.IsNullOrWhiteSpace(entry.Member));
                Assert.False(string.IsNullOrWhiteSpace(entry.Reason));
                Assert.True(
                    DateOnly.TryParse(entry.Date, out _),
                    $"{entry.Member}'s exemption date \"{entry.Date}\" is not a valid date.");
            }
        }

        [Fact]
        public void EveryExemptionsMemberResolvesToARealProductionType()
        {
            // TheSeamListIsANamedConstantInTheSuite's own sibling check (Story291_ConventionLaws.cs)
            // proves the same disease: NotEmpty alone lets a deleted/typo'd entry match nothing,
            // silently. ProductionAssemblies.HasType is the one shared "does this name resolve"
            // mechanism both lists lean on (STORY-291 review).
            foreach (var entry in ExemptionBaseline.Entries)
            {
                Assert.True(
                    ProductionAssemblies.HasType(entry.Member),
                    $"\"{entry.Member}\" does not resolve to any loaded production type — a phantom " +
                    "exemption entry would silently match nothing.");
            }
        }

        [Fact]
        public void ANewViolationFailsDespiteTheBaseline()
        {
            var baselined = new LawViolation(LawId.L2, "Fixture.Baselined", "does depend on \"Npgsql.NpgsqlConnection\"");
            var newViolation = new LawViolation(LawId.L2, "Fixture.New", "does depend on \"Npgsql.NpgsqlConnection\"");

            var localBaseline = new[]
            {
                new ArchitectureExemption(
                    LawId.L2, "Fixture.Baselined", "2026-08-07",
                    "test-local baseline entry proving the filter, not a real exemption"),
            };

            var unexempted = DependencyLawAssert.FindUnexempted([baselined, newViolation], localBaseline);
            var survivor = Assert.Single(unexempted);
            Assert.Equal("Fixture.New", survivor.Member);

            // AssertNone actually fails when a new violation survives filtering...
            var failure = Record.Exception(() =>
                DependencyLawAssert.AssertNone([baselined, newViolation], localBaseline));
            Assert.NotNull(failure);
            Assert.Contains(LawId.L2, failure.Message);
            Assert.Contains("Fixture.New", failure.Message);
            Assert.DoesNotContain("Fixture.Baselined", failure.Message);

            // ...and raises nothing when every violation is baselined.
            DependencyLawAssert.AssertNone([baselined], localBaseline);
        }
    }
}
