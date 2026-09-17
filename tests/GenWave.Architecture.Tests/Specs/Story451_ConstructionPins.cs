// STORY-451 — One construction path — the pins (gh-#401 · SPEC F184.3–F184.5 · PLAN T510, T513, T514)
//
// BDD specification — xUnit. AC4 scans src/ and tests/ for `new Orchestrator(`; AC5 reads Host.Tests' csproj and file list; AC8 reflects the
// TestSupport assembly for [Fact] methods.
//
// RED at plan time: every fact was [Fact(Skip = Pending)] with a loud body — remove the Skip only in
// the task that makes it green. T513 un-skips ScenarioHostTestsAfterTheMove's three facts;
// ScenarioTheTextScanForConstruction's three facts stay skipped until T514 lands the two-site text scan.

using System.Reflection;
using System.Xml.Linq;
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureConstructionPins
{
    const string Pending = "pending: T514 — two `new Orchestrator(` sites (STORY-451)";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheTextScanForConstruction
    {
        // Given: every .cs under src/ and tests/ scanned for `new Orchestrator(`

        /// <summary>AC4 — the builder is one site</summary>
        [Fact(Skip = Pending)]
        public void HitsOrchestratorBuilder() => Assert.Fail(Pending);

        /// <summary>AC4 — AddOrchestration is the other site</summary>
        [Fact(Skip = Pending)]
        public void HitsTheServiceCollectionExtensions() => Assert.Fail(Pending);

        /// <summary>AC4 — exactly two files hit</summary>
        [Fact(Skip = Pending)]
        public void HitsNothingElse() => Assert.Fail(Pending);
    }

    public sealed class ScenarioHostTestsAfterTheMove
    {
        // Given: GenWave.Host.Tests.csproj and its file tree

        static readonly string HostTestsDir =
            Path.Combine(SolutionLocator.Root(), "tests", "GenWave.Host.Tests");

        /// <summary>AC5 — the ProjectReference is present</summary>
        [Fact]
        public void ReferencesTestSupport()
        {
            var csprojPath = Path.Combine(HostTestsDir, "GenWave.Host.Tests.csproj");
            var csproj = XDocument.Load(csprojPath);

            var referencesTestSupport = csproj.Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Any(include => include is not null
                    && include.EndsWith("GenWave.TestSupport.csproj", StringComparison.Ordinal));

            Assert.True(referencesTestSupport, $"{csprojPath} carries no ProjectReference to GenWave.TestSupport");
        }

        /// <summary>AC5 — the duplicate fake is deleted</summary>
        [Fact]
        public void NoLongerCarriesFakeRenderBudgetProvider() =>
            Assert.Empty(Directory.EnumerateFiles(HostTestsDir, "FakeRenderBudgetProvider.cs", SearchOption.AllDirectories));

        /// <summary>AC5 — the duplicate fake is deleted</summary>
        [Fact]
        public void NoLongerCarriesFakeBoundaryBiasProvider() =>
            Assert.Empty(Directory.EnumerateFiles(HostTestsDir, "FakeBoundaryBiasProvider.cs", SearchOption.AllDirectories));
    }

    public sealed class ScenarioTheSupportAssembly
    {
        // Given: GenWave.TestSupport reflected

        /// <summary>AC8 — the project carries fakes, not facts</summary>
        [Fact]
        public void HasNoFactMethods()
        {
            var assembly = typeof(GenWave.TestSupport.AssemblyMarker).Assembly;

            var factMethods = assembly.GetTypes()
                .SelectMany(type => type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(method => method.GetCustomAttributes(inherit: true)
                    .Any(attribute => attribute is Xunit.FactAttribute))
                .ToList();

            Assert.Empty(factMethods);
        }
    }
}
