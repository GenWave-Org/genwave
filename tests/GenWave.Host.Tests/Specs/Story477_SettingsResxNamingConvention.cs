// STORY-477 — resx-name-matches-marker-type-name naming convention (gh-#778 · PLAN T573 round 2/4)
//
// BDD specification — xUnit. Pins the fix for the review finding that a LogicalName override on
// EmbeddedResource resolves a neutral resx's manifest name but leaves each culture-suffixed
// satellite assembly under its OWN physical-filename-derived name — so a shipped
// Settings.fr.resx resolved to a resource name with no "fr" suffix, and "fr" silently fell back to
// en. Proven here against a THROWAWAY test-project marker/resx pair
// (TestSettingsResources[.fr].resx), not the Host's own (currently untranslated)
// SettingsResources.resx, through the same real
// Microsoft.Extensions.Localization.ResourceManagerStringLocalizerFactory AddLocalization()
// registers in production. See TestSettingsResources's own remarks.
//
// Round 4: one IAsyncLifetime Scenario class per Given — arrange + act in InitializeAsync
// (restoring culture in a finally there, as ScenarioAGermanAcceptLanguage in
// Story477_SettingsLocalizationCulturePinning.cs does), temp dirs deleted in DisposeAsync, each
// fact body reduced to its one assert.

using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Support.Localization;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSettingsresxnamingconvention
{
    static IStringLocalizer<TestSettingsResources> CreateLocalizer()
    {
        // Fully qualified, deliberately not `using`-imported: an unqualified "Options" here
        // would resolve to the sibling GenWave.Host.Options namespace, not the
        // Microsoft.Extensions.Options.Options static factory — this file's own
        // GenWave.Host.Tests.Specs namespace nests under GenWave.Host, so that sibling
        // namespace wins over any type this file could otherwise reach unqualified.
        var factory = new ResourceManagerStringLocalizerFactory(
            Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<TestSettingsResources>(factory);
    }

    static string SatelliteFileName =>
        $"{typeof(SettingsResources).Assembly.GetName().Name}.resources.dll";

    public sealed class ScenarioAFrenchUiCulture : IAsyncLifetime
    {
        // Given: a real ResourceManagerStringLocalizerFactory over the TestSettingsResources marker,
        // whose resx file name matches the marker type's own name (Configuration/SettingsResources.cs's
        // own remarks explain why that match matters), run under UI culture "fr"

        string greetingValue = "";
        bool greetingResourceNotFound = true;

        public Task InitializeAsync()
        {
            var localizer = CreateLocalizer();
            var originalCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
                LocalizedString greeting = localizer["Greeting"];
                greetingValue = greeting.Value;
                greetingResourceNotFound = greeting.ResourceNotFound;
            }
            finally
            {
                CultureInfo.CurrentUICulture = originalCulture;
            }
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>Pins the naming convention: a fr satellite resx compiled alongside the neutral
        /// resx is found under fr, not silently skipped like the LogicalName route the reviewer
        /// found broken.</summary>
        [Fact]
        public void ServesTheFrenchValueUnderFrenchUiCulture() =>
            Assert.Equal(("Bonjour", false), (greetingValue, greetingResourceNotFound));
    }

    public sealed class ScenarioAnEnglishUiCulture : IAsyncLifetime
    {
        // Given: the same real ResourceManagerStringLocalizerFactory over the TestSettingsResources
        // marker, run under UI culture "en" — proves the fr case above is a genuine satellite
        // resolution and not an accidental match against the neutral value

        string greetingValue = "";
        bool greetingResourceNotFound = true;

        public Task InitializeAsync()
        {
            var localizer = CreateLocalizer();
            var originalCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
                LocalizedString greeting = localizer["Greeting"];
                greetingValue = greeting.Value;
                greetingResourceNotFound = greeting.ResourceNotFound;
            }
            finally
            {
                CultureInfo.CurrentUICulture = originalCulture;
            }
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>The neutral resx still serves its own value under en, so the fr case above is a
        /// genuine satellite resolution and not an accidental match against the neutral value.</summary>
        [Fact]
        public void ServesTheNeutralValueUnderEnglishUiCulture() =>
            Assert.Equal(("Hello", false), (greetingValue, greetingResourceNotFound));
    }

    public sealed class ScenarioADiscoverFixtureDirectory : IAsyncLifetime
    {
        // Given: a scratch directory laid out like a publish output — an "en" floor with no
        // satellite needed, a "fr" satellite carrying SettingsResources's own compiled resource name,
        // a "de" directory carrying an unrelated dependency's satellite, and a non-culture directory
        // that must not abort the scan (PLAN T573 round 2, R3: SettingsCultures.Discover pulled out
        // of Program.cs so it can be tested directly)

        string fixtureDirectory = "";
        List<string> cultureNames = new();

        public Task InitializeAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "genwave-settings-cultures-" + Guid.NewGuid());
            var frDirectory = Directory.CreateDirectory(Path.Combine(root, "fr"));
            File.WriteAllBytes(Path.Combine(frDirectory.FullName, SatelliteFileName), Array.Empty<byte>());

            // A culture directory some OTHER dependency shipped a satellite for — must not surface
            // as a supported settings culture just because a directory happens to be named "de".
            var deDirectory = Directory.CreateDirectory(Path.Combine(root, "de"));
            File.WriteAllBytes(Path.Combine(deDirectory.FullName, "SomeOtherLibrary.resources.dll"), Array.Empty<byte>());

            // A directory whose name isn't a culture at all — must be skipped, not throw.
            Directory.CreateDirectory(Path.Combine(root, "not-a-culture-name"));

            fixtureDirectory = root;
            cultureNames = SettingsCultures.Discover(fixtureDirectory).Select(c => c.Name).ToList();
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            Directory.Delete(fixtureDirectory, recursive: true);
            return Task.CompletedTask;
        }

        /// <summary>AC-equivalent for R3: "en" is always the floor, a real satellite for the
        /// SettingsResources assembly is discovered, an unrelated dependency's satellite for the
        /// same directory name is not, and a non-culture directory name does not fail the scan.</summary>
        [Fact]
        public void DiscoversEnglishAndTheRealFrenchSatelliteOnly() =>
            Assert.Equal(new[] { "en", "fr" }, cultureNames.OrderBy(n => n, StringComparer.Ordinal));
    }

    public sealed class ScenarioAnEnglishSatelliteAlsoShips : IAsyncLifetime
    {
        // Given: an explicit "en/" satellite directory (unusual, but not impossible — a build that
        // treats "en" as a translated culture rather than the neutral resource)

        string root = "";
        List<string> cultureNames = new();

        public Task InitializeAsync()
        {
            root = Path.Combine(Path.GetTempPath(), "genwave-settings-cultures-en-" + Guid.NewGuid());
            var enDirectory = Directory.CreateDirectory(Path.Combine(root, "en"));
            File.WriteAllBytes(Path.Combine(enDirectory.FullName, SatelliteFileName), Array.Empty<byte>());

            cultureNames = SettingsCultures.Discover(root).Select(c => c.Name).ToList();
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            Directory.Delete(root, recursive: true);
            return Task.CompletedTask;
        }

        /// <summary>An explicit "en/" satellite directory does not duplicate the "en" floor Discover
        /// already seeds the result with.</summary>
        [Fact]
        public void DoesNotDuplicateEnglishWhenAnEnglishSatelliteAlsoShips() =>
            Assert.Equal(new[] { "en" }, cultureNames);
    }

    public sealed class ScenarioAMissingBaseDirectory : IAsyncLifetime
    {
        // Given: a base directory that does not exist (nothing published there yet)

        List<string> cultureNames = new();

        public Task InitializeAsync()
        {
            var missingDirectory = Path.Combine(Path.GetTempPath(), "genwave-settings-cultures-missing-" + Guid.NewGuid());
            cultureNames = SettingsCultures.Discover(missingDirectory).Select(c => c.Name).ToList();
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>A missing base directory still yields the "en" floor instead of throwing.</summary>
        [Fact]
        public void FallsBackToEnglishWhenTheBaseDirectoryIsMissing() =>
            Assert.Equal(new[] { "en" }, cultureNames);
    }
}
