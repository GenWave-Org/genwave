using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Measures intro and outro energy levels for a media file at library scan time (STORY-029, Epic H).
/// This is the offline path — it must never run on the real-time playout loop.
/// </summary>
public interface IEnergyAnalyzer
{
    Task<EnergyPoints?> AnalyzeAsync(string path, double? cueInSec, double? cueOutSec, CancellationToken ct);
}
