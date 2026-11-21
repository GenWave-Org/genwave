using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IStationIdentityProvider"/> seam (SPEC F44.1, gitea-#196): wraps
/// <see cref="IOptionsMonitor{TOptions}"/> so every identity consumer — the Orchestrator's
/// <c>SegmentRequest</c> stamping, <c>GET /api/stations</c>, and the playout push path — reads the
/// SAME live value <c>PUT /api/settings</c> writes (mirrors <see cref="OptionsMonitorCadenceProvider"/>
/// and <see cref="OptionsMonitorRotationSettingsProvider"/>).
///
/// Replaces the boot-frozen <c>StationContext</c> singleton Program.cs used to build once from
/// <c>IOptions&lt;StationOptions&gt;</c>: <c>Station:Name</c> and <c>Station:Voice</c> are advertised
/// <c>Live</c> in the settings allowlist, so a value read once at boot and never again is exactly the
/// class of staleness bug F30/gitea-#211 fixed for scope and cadence.
///
/// Builds a new <see cref="StationIdentity"/> from <see cref="StationOptions"/> on every call —
/// nothing is cached here — <see cref="IOptionsMonitor{T}.CurrentValue"/> already is the cache, and
/// caching again would reintroduce the boot-snapshot staleness this seam exists to fix.
///
/// <see cref="StationIdentity.Voice"/> is the voice id for the ACTIVE engine (SPEC F219.8, PLAN T753):
/// <c>Station:Voice</c> may be a bare id or an engine map, so the raw setting is never handed on. A map with no
/// entry for the active engine reads as <see cref="StationIdentity.VoiceMissing"/> with an empty voice.
/// </summary>
sealed class OptionsMonitorStationIdentityProvider(
    IOptionsMonitor<StationOptions> stationMonitor,
    IVoiceResolver voiceResolver) : IStationIdentityProvider
{
    public StationIdentity Current
    {
        get
        {
            var station = stationMonitor.CurrentValue;
            var voice = voiceResolver.ResolveSetting(station.Voice);
            return new StationIdentity(station.Id, station.Name, voice ?? string.Empty) { VoiceMissing = voice is null };
        }
    }
}
