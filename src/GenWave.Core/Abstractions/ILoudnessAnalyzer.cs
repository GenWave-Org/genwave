using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Measures a file's loudness at ingest (PRD §4.3). This is the offline path — it must never run on
/// the real-time playout loop.
/// </summary>
public interface ILoudnessAnalyzer
{
    Task<Loudness> AnalyzeAsync(string path, CancellationToken ct);
}
