using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Mutable <see cref="IStationImagingSettingsProvider"/> double (SPEC F110.1/F110.3, STORY-301/302,
/// PLAN T230) — mirrors <see cref="FakeContextSettingsProvider"/>'s own shape one seam over. Starts
/// both-false, the same fail-closed posture <see cref="NoOpStationImagingSettingsProvider"/> answers
/// with — a spec sets <see cref="Current"/> to opt one or both knobs in.
/// </summary>
sealed class FakeStationImagingSettingsProvider : IStationImagingSettingsProvider
{
    public StationImagingSettings Current { get; set; } = new(ClockAnchoredIdents: false, TimeAnnouncements: false);
}
