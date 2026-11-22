namespace GenWave.Host.Voices;

/// <summary>One entry of the shipped <c>starter.json</c> manifest (SPEC F219.14): a starter clip's slug, label, WAV file name, length and credit line.</summary>
public sealed record StarterClipEntry(string Slug, string Label, string File, int DurationMs, string Credit);
