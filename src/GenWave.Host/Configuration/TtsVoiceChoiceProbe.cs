using GenWave.Core.Abstractions;
using GenWave.Tts;
using Microsoft.Extensions.Options;

namespace GenWave.Host.Configuration;

/// <summary>
/// Live <see cref="IChoiceProbe"/> for <c>Station:Voice</c> (SPEC F205.7, STORY-479, PLAN T580) —
/// wraps the SAME <see cref="ITtsVoiceLister"/> singleton <c>GET /api/voices</c> reads (production:
/// <c>CachedVoiceLister</c> over <c>KokoroVoiceLister</c>), scoped by the live <c>Tts:Endpoint</c>
/// value so a repoint invalidates <see cref="ProbedChoiceCache"/>'s own 60 s entry immediately
/// rather than serving the OLD endpoint's voice list (see <see cref="IChoiceProbe.ScopeKey"/>'s own
/// remarks). Unlike <see cref="LlmModelChoiceProbe"/>'s LLM sibling, <c>Tts:Endpoint</c> has no
/// "disabled" state to special-case (SPEC F36.1) — any fault (transport, malformed response) simply
/// throws through, exactly the same as every other <see cref="IChoiceProbe"/>.
/// <see cref="ITtsVoiceLister"/> arrives wrapped in <see cref="Lazy{T}"/> (Program.cs) — resolving
/// THIS probe (as <see cref="ChoiceSourceBootCheck"/> does, once, at boot) must never itself build
/// the live lister's typed <c>HttpClient</c>; only an actual <see cref="FetchAsync"/> call does.
/// </summary>
internal sealed class TtsVoiceChoiceProbe(
    Lazy<ITtsVoiceLister> voiceLister, IOptionsMonitor<TtsOptions> optionsMonitor) : IChoiceProbe
{
    /// <summary>The <see cref="StationSettingsAllowlist"/> <c>Station:Voice</c> entry's own
    /// <see cref="SettingChoiceSource.Probe.Name"/> — also what <see cref="ChoiceSourceBootCheck"/>
    /// checks the allowlist against.</summary>
    public const string ProbeName = "tts-voices";

    public string Name => ProbeName;

    public string ScopeKey() => optionsMonitor.CurrentValue.Endpoint;

    public async Task<IReadOnlyList<SettingChoice>> FetchAsync(CancellationToken ct)
    {
        var voiceIds = await voiceLister.Value.ListVoicesAsync(ct).ConfigureAwait(false);
        return voiceIds.Select(id => new SettingChoice(id, id)).ToList();
    }
}
