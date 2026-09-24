// STORY-477 — settings localization pins the format culture to invariant (gh-#778 · PLAN T573 round
// 3, review finding F1)
//
// BDD specification — xUnit. The review found
// Story477_SettingsDescriptorsTableAndCopy.cs's ScenarioTheValidatorUnderAGermanAcceptLanguage could
// never fail: only "en" ships as a SettingsResources satellite, so Accept-Language: de was never
// actually selected as a UI culture, AND BedDuckDb's (-60, 0) bounds print identically in every
// culture anyway (no decimal separator to leak). That HTTP-level fact was deleted. This spec proves
// the SAME invariant — SettingsLocalization.CreateOptions pins SupportedCultures to
// CultureInfo.InvariantCulture no matter which UI culture Accept-Language asks for — directly
// against the real Microsoft.AspNetCore.Localization.RequestLocalizationMiddleware, with "de"
// genuinely offered as a SupportedUICulture, so a regression that widened SupportedCultures back to
// the discovered-cultures list (PLAN T573 round 1's shape) turns this fact red. See this file's own
// build/round-1 red-run proof in the PLAN T573 round 3 report.

using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Configuration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSettingslocalizationculturepinning
{
    public sealed class ScenarioAGermanAcceptLanguage : IAsyncLifetime
    {
        // Given: SettingsLocalization.CreateOptions offering [en, de] as UI cultures, and a request
        // carrying Accept-Language: de, run through the real RequestLocalizationMiddleware

        static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
        static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

        CultureInfo capturedCulture = CultureInfo.InvariantCulture;
        CultureInfo capturedUiCulture = CultureInfo.InvariantCulture;

        public async Task InitializeAsync()
        {
            var options = SettingsLocalization.CreateOptions(new[] { English, German });
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["Accept-Language"] = "de";

            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                var middleware = new RequestLocalizationMiddleware(
                    next: _ =>
                    {
                        capturedCulture = CultureInfo.CurrentCulture;
                        capturedUiCulture = CultureInfo.CurrentUICulture;
                        return Task.CompletedTask;
                    },
                    // Fully qualified: unqualified "Options" here would resolve to the sibling
                    // GenWave.Host.Options namespace, not the Microsoft.Extensions.Options.Options
                    // static factory — this file's own GenWave.Host.Tests.Specs namespace nests
                    // under GenWave.Host, so that sibling namespace wins over any unqualified use.
                    Microsoft.Extensions.Options.Options.Create(options),
                    NullLoggerFactory.Instance);

                await middleware.Invoke(httpContext);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>
        /// F1 — Accept-Language: de picks a UI (copy) culture that genuinely IS offered
        /// (SupportedUICultures carries it), yet the format culture the downstream request runs
        /// under stays pinned to CultureInfo.InvariantCulture: SupportedCultures never follows
        /// Accept-Language. One assert covering both cultures at once.
        /// </summary>
        [Fact]
        public void PinsTheFormatCultureToInvariantWhileServingTheGermanUiCulture() =>
            Assert.Equal((CultureInfo.InvariantCulture, German), (capturedCulture, capturedUiCulture));
    }
}
