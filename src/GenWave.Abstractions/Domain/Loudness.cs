namespace GenWave.Core.Domain;

/// <summary>
/// Precomputed loudness for one track (PRD §4.3). Measured once at ingest via FFmpeg ebur128 and
/// applied as a gain offset at playout — never re-measured on the hot path.
/// </summary>
/// <param name="IntegratedLufs">Integrated program loudness (ITU-R BS.1770 / EBU R128).</param>
/// <param name="TruePeakDbtp">Oversampled true peak in dBTP.</param>
/// <param name="Measurable">
/// False when the file is silent/gated (≈ −70 LUFS) or too short to measure. Such tracks receive
/// zero gain, so "boost to target" never detonates a near-silent file (PRD §4.1).
/// </param>
public readonly record struct Loudness(double IntegratedLufs, double TruePeakDbtp, bool Measurable);
