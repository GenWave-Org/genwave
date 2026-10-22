namespace GenWave.Loudness;

/// <summary>The outcome of an ffmpeg run that reports instead of throwing: its exit code and everything it wrote to stderr.</summary>
public sealed record FfmpegRunResult(int ExitCode, string Stderr)
{
    public bool Succeeded => ExitCode == 0;
}
