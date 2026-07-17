using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Detects silence-trimmed cue points for a media file at library scan time (PRD §4.3).
/// This is the offline path — it must never run on the real-time playout loop.
/// </summary>
public interface ICueAnalyzer
{
    Task<CuePoints?> AnalyzeAsync(string path, CancellationToken ct);
}
