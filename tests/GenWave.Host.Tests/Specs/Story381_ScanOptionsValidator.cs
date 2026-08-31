// PLAN T381 (SPEC F154.3, STORY-379, gh-#529) — Library:Scan:QuarantineExemptRoots must be absolute
// paths at boot. ScanOptions is bound via a plain Configure<T> call inside
// MediaLibraryServiceCollectionExtensions (never AddOptions<T>().Bind().ValidateDataAnnotations()),
// so ScanOptionsValidator is the only thing that actually enforces this — the same
// "documentation-only [Range], this validator is the real floor" story
// Story321_TimeAnnouncementBudgetSecondsValidation.cs's own header already tells for
// StationOptionsValidator, applied here to a different nested options class.
//
// BDD specification — xUnit. Direct validator construction/invocation (Story321's own idiom) pins
// the pure Validate() rule; T381 review N6 adds a REAL boot proof on top — a WebApplicationFactory
// whose ONLY override is the bad key, asserting the framework's own ValidateOnStart() wiring in
// Program.cs actually calls this validator (Story380_TheKnobsAndTheLiveSwitch.cs's own
// `Assert.Throws<OptionsValidationException>(() => factory.Services)` idiom for the SAME reason:
// Validate() passing in isolation says nothing about whether Program.cs ever registered the
// trigger).

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using GenWave.Host.Options;
using GenWave.MediaLibrary.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureScanOptionsValidator
{
    static ScanOptionsValidator BuildValidator() => new();

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioEveryExemptRootIsAbsolute
    {
        [Fact]
        public void BootValidationAcceptsTheDefault()
        {
            var result = BuildValidator().Validate(null, new ScanOptions());

            Assert.True(result.Succeeded);
        }

        [Fact]
        public void BootValidationAcceptsAnEmptySet()
        {
            var result = BuildValidator().Validate(null, new ScanOptions { QuarantineExemptRoots = [] });

            Assert.True(result.Succeeded);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioARelativeExemptRootFailsBoot
    {
        [Fact]
        public void BootValidationRejectsARelativeRoot()
        {
            var options = new ScanOptions { QuarantineExemptRoots = ["authored"] };

            var result = BuildValidator().Validate(null, options);

            Assert.True(result.Failed);
        }

        [Fact]
        public void TheFailureNamesTheConfigKey()
        {
            var options = new ScanOptions { QuarantineExemptRoots = ["/authored", "relative/path"] };

            var result = BuildValidator().Validate(null, options);

            Assert.Contains("Library:Scan:QuarantineExemptRoots", result.FailureMessage ?? string.Empty, StringComparison.Ordinal);
        }
    }

    // T381 review N6 — the REAL production binary, not just the validator in isolation.
    public sealed class ScenarioARelativeExemptRootFailsARealBoot
    {
        [Fact]
        public void TheHostThrowsOptionsValidationExceptionOnFirstResolve()
        {
            using var factory = new ScanOptionsWebFactory("relative/bad/root");

            Assert.Throws<OptionsValidationException>(() => factory.Services);
        }

        [Fact]
        public void TheExceptionNamesTheConfigKey()
        {
            using var factory = new ScanOptionsWebFactory("relative/bad/root");

            var ex = Assert.Throws<OptionsValidationException>(() => factory.Services);

            Assert.Contains("Library:Scan:QuarantineExemptRoots", ex.Message, StringComparison.Ordinal);
        }
    }
}

/// <summary>
/// T381 review N6's own DB-less factory (mirrors Story380's own <c>GardenerKnobsWebFactory</c>) —
/// <c>ConnectionStrings:Library</c> is never actually reached: <c>ValidateOnStart()</c> fires the
/// instant <c>factory.Services</c> is first touched, well before any request (or this suite's own
/// hosted-service removal) would ever need a real connection.
/// </summary>
file sealed class ScanOptionsWebFactory(string exemptRoot) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t381-scan-options-boot";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config supplies Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint
        // (Story380's own precedent), so ValidateOnStart() is satisfied for every OTHER options
        // class without this factory injecting them manually — only the one key under test here.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Library:Scan:QuarantineExemptRoots:0", exemptRoot);

        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}
