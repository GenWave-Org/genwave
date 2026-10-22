namespace GenWave.VoiceOff.Timing;

/// <summary>Peak footprint over a sampling window, in whole MB. Null means never measured.</summary>
internal sealed record ResourceUsage(long? PeakRssMb, long? PeakVramMb);
