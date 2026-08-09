using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IContextSettingsProvider"/> seam (SPEC F107.2, PLAN T226):
/// reads <c>Context:{Key}:Enabled|SegmentCadenceMinutes|PatterCadenceMinutes|PersonaId</c> straight
/// off <see cref="IConfiguration"/> rather than a typed <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
/// binding, unlike every sibling provider in this folder — hence <c>Configuration</c>, not
/// <c>OptionsMonitor</c>, in this class's own name. <see cref="Core.Abstractions.IContextProvider.Key"/>
/// is not a fixed, known-in-advance set of properties — F107.1's own contract is "any future kind"
/// — so a typed options class would need a new property added every time a provider joins the
/// pipeline; <see cref="IConfiguration"/>'s own indexer/<c>GetValue</c> reads are already this
/// codebase's established "no options class needed, read fresh, case-insensitive" idiom
/// (<c>SettingValidator</c>/<c>SettingsController</c> both index <see cref="IConfiguration"/>
/// directly for the same reason). Nothing is cached on this instance — a live PUT to
/// <c>Context:Weather:Enabled</c> reaches the very next tick with no restart, the same freshness
/// discipline every sibling provider in this folder follows via <c>IOptionsMonitor.CurrentValue</c>.
///
/// <para>
/// <b>Deliberately provider-agnostic (F4 fix, T226 review).</b> This class used to special-case
/// <c>key == "weather"</c> to clamp <c>SegmentCadenceMinutes</c> to SPEC F108.2's 30-minute floor —
/// the exact kind of one-provider knowledge a generic, "any future kind" settings shim must never
/// carry. That floor now lives on <c>GenWave.Context.Weather.WeatherContextProvider</c> itself, as
/// an opt-in <c>GenWave.Context.ICadenceFlooredContextProvider</c> capability
/// <c>GenWave.Context.ContextPipeline</c> consults directly — the structural backstop for a value
/// that reaches the pipeline some way other than <c>PUT /api/settings</c> (an appsettings.json/env
/// override, which never passes through <see cref="Configuration.SettingValidator"/>'s own
/// write-time 30–1440 range at all). This class reads exactly what configuration holds, for any key,
/// with no per-provider exception.
/// </para>
/// </summary>
sealed class ConfigurationContextSettingsProvider(IConfiguration configuration) : IContextSettingsProvider
{
    const bool DefaultEnabled = false;
    const int DefaultSegmentCadenceMinutes = 60;
    const int DefaultPatterCadenceMinutes = 0;

    public ContextProviderSettings For(string key)
    {
        var enabled = configuration.GetValue($"Context:{key}:Enabled", DefaultEnabled);
        var segmentCadenceMinutes = configuration.GetValue($"Context:{key}:SegmentCadenceMinutes", DefaultSegmentCadenceMinutes);
        var patterCadenceMinutes = configuration.GetValue($"Context:{key}:PatterCadenceMinutes", DefaultPatterCadenceMinutes);
        var personaId = configuration.GetValue<long?>($"Context:{key}:PersonaId", null);

        return new ContextProviderSettings(enabled, segmentCadenceMinutes, patterCadenceMinutes, personaId);
    }
}
