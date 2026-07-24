using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IRequestOverrideEnvelopeProvider"/> seam (SPEC F87.6,
/// STORY-227, PLAN T90): wraps <see cref="IOptionsMonitor{TOptions}"/> so the fulfillment rung reads
/// the SAME live value <c>PUT /api/settings</c> writes (mirrors
/// <see cref="OptionsMonitorEnvelopeProvider"/>). Nothing is cached here —
/// <see cref="IOptionsMonitor{T}.CurrentValue"/> already is the cache.
/// </summary>
sealed class OptionsMonitorRequestOverrideEnvelopeProvider(IOptionsMonitor<StationOptions> stationMonitor)
    : IRequestOverrideEnvelopeProvider
{
    public bool Current => stationMonitor.CurrentValue.Requests.OverrideEnvelope;
}
