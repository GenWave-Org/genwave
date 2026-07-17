namespace GenWave.Core.Abstractions;

/// <summary>
/// Estimates a media file's tempo (BPM) for a cue-trimmed body at library scan time (SPEC F46, closes
/// gitea-#190). This is the offline path — it must never run on the real-time playout loop.
///
/// Half/double-time ambiguity (e.g. a waltz reading 180 instead of 90) is accepted and unresolved by
/// this seam: BPM is advisory metadata this phase, with no selection or transition consumer (F46.6).
/// </summary>
public interface IBpmAnalyzer
{
    Task<double?> AnalyzeAsync(string path, double? cueInSec, double? cueOutSec, CancellationToken ct);
}
