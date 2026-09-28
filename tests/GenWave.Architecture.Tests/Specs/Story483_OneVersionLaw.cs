// STORY-483 — One version, read once, shown one way (gh-#9 + gh-#868 · SPEC F211.1, F211.7 · PLAN T590)
//
// BDD specification — xUnit. Reads what SHIPS: the production assemblies' IL/source, not a list of
// file names — same detector family (HttpClientMetadataScan, reflection over ProductionAssemblies)
// every other convention law in this suite already uses (Story291_ConventionLaws.cs).

using GenWave.Architecture.Tests.Support;
using GenWave.Host.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureOneVersionLaw
{
    public sealed class ScenarioOneReader
    {
        // Given: every production assembly's types that read AssemblyInformationalVersionAttribute

        static readonly (string Namespace, string Name)[] ForbiddenTypes =
        {
            ("System.Reflection", "AssemblyInformationalVersionAttribute"),
            ("System.Diagnostics", "FileVersionInfo"),
        };

        /// <summary>AC9 — the only reader is the IAppVersion provider
        /// (<see cref="GenWave.Core.AppVersion.FromAssembly"/>). Reuses
        /// <see cref="HttpClientMetadataScan"/> unchanged: its "does any type's field/parameter/
        /// return/local/IL reference this metadata token" question is exactly the same shape as L3's
        /// HttpClient-family forbid, just pointed at a different pair of types. This scan matches a
        /// DIRECT type reference in the compiled metadata tables (a field/parameter/return/local typed
        /// as the forbidden type, or its token appearing in IL). Known blind spot (shared with every
        /// other <see cref="HttpClientMetadataScan"/>-based law in this suite): a reader that never
        /// references the forbidden type directly at all — e.g. one that compares
        /// <see cref="System.Reflection.CustomAttributeData.AttributeType"/>'s <c>Name</c> (or a
        /// similarly indirect name lookup) against the string "AssemblyInformationalVersionAttribute"
        /// or "FileVersionInfo" — would not surface here.</summary>
        [Fact]
        public void OnlyTheProviderReads()
        {
            var readers = ProductionAssemblies.AllProductionAssemblies()
                .SelectMany(assembly => HttpClientMetadataScan.FindReferencingTypes(assembly.Location, ForbiddenTypes))
                .Select(hit => hit.TypeFullName)
                .Distinct()
                .OrderBy(typeFullName => typeFullName, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(new[] { "GenWave.Core.AppVersion" }, readers);
        }
    }

    public sealed class ScenarioTheOldReadersAreGone
    {
        // Given: every production assembly's type names

        /// <summary>AC10 — no HostVersion</summary>
        [Fact]
        public void NoHostVersion() => AssertNoTypeNamed("HostVersion");

        /// <summary>AC10 — no AdAppVersion</summary>
        [Fact]
        public void NoAdAppVersion() => AssertNoTypeNamed("AdAppVersion");

        static void AssertNoTypeNamed(string name)
        {
            var matches = ProductionAssemblies.AllProductionAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.Name == name)
                .Select(type => type.FullName)
                .ToList();

            Assert.Empty(matches);
        }
    }

    public sealed class ScenarioNoContractChange
    {
        // Given: PublicSurface.Of(GenWave.Abstractions) vs the published-package fixture, both read
        // once here rather than inside either fact below (T590 review).

        const string DiffHeading = "GenWave.Abstractions public surface differs from the 5.10.0 baseline:";

        readonly IReadOnlyList<string> baseline;
        readonly IReadOnlyList<string> diffLines;

        public ScenarioNoContractChange()
        {
            baseline = PublicSurface.ReadBaseline(BaselinePath());
            var current = PublicSurface.Of(ProductionAssemblies.Abstractions);
            diffLines = PublicSurface.DiffAgainst(current, baseline, DiffHeading);
        }

        static string BaselinePath() =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "abstractions-5.10.0-surface.txt");

        /// <summary>The fixture itself is a real enumeration, not an empty/truncated file — a
        /// precondition for <see cref="AbstractionsSurfaceUnchanged"/> below, not itself part of
        /// AC11.</summary>
        [Fact]
        public void TheBaselineFixtureIsNotEmpty() =>
            Assert.True(baseline.Count > 20, $"the baseline fixture has only {baseline.Count} lines — too small to be a real enumeration.");

        /// <summary>AC11 (F211.7) — T590 touches no type in <c>src/GenWave.Abstractions</c>, so the
        /// published package's public surface must still match the 5.10.0 baseline byte for byte.
        /// Same fixture and detector <c>Story431_SponsorSettingsAndRelease.ScenarioAbstractionsUnchanged
        /// .ThePackageSurfaceDiffVs5100IsEmpty</c> (GenWave.Host.Tests) already runs — linked into this
        /// project (<see cref="PublicSurface"/>, the fixture file) rather than copied, so STORY-483's
        /// own acceptance criterion has its own green fact in the architecture suite without a second,
        /// independently-maintained enumerator.</summary>
        [Fact]
        public void AbstractionsSurfaceUnchanged() => Assert.Empty(diffLines);
    }
}
