using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace GenWave.Host.Configuration;

/// <summary>
/// Builds the <see cref="RequestLocalizationOptions"/> Program.cs installs via
/// <c>UseRequestLocalization</c> (SPEC F205.2, STORY-477, PLAN T573) — pulled out of Program.cs so
/// its culture-pinning shape can be exercised directly against the real
/// <see cref="RequestLocalizationMiddleware"/> instead of only through the whole host's boot. See
/// Program.cs's own comment at the <c>UseRequestLocalization</c> call site for why the pipeline
/// places it where it does and why <c>AcceptLanguageHeaderRequestCultureProvider</c> is the only
/// registered provider.
/// </summary>
public static class SettingsLocalization
{
    /// <summary>
    /// <paramref name="uiCultures"/> (typically <see cref="SettingsCultures.Discover"/>'s result)
    /// becomes <see cref="RequestLocalizationOptions.SupportedUICultures"/> — the only axis a
    /// client's Accept-Language is allowed to move. <see cref="RequestLocalizationOptions.SupportedCultures"/>
    /// stays pinned to <see cref="CultureInfo.InvariantCulture"/> alone, so
    /// <see cref="CultureInfo.CurrentCulture"/> (number/date parsing and formatting — SettingValidator's
    /// range checks, JSON numeric bodies) never follows Accept-Language; only
    /// <see cref="CultureInfo.CurrentUICulture"/> (SettingCopy's resx lookups) does. An
    /// Accept-Language that picks a fr/de resx label must not ALSO flip "-60" into a
    /// "-60,0"-style parse on the way in.
    /// </summary>
    public static RequestLocalizationOptions CreateOptions(IReadOnlyList<CultureInfo> uiCultures) =>
        new()
        {
            DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("en")),
            SupportedCultures = new[] { CultureInfo.InvariantCulture },
            SupportedUICultures = uiCultures.ToList(),
            RequestCultureProviders = new List<IRequestCultureProvider> { new AcceptLanguageHeaderRequestCultureProvider() },
        };
}
