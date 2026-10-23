using GenWave.VoiceOff.Audio;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Render;

/// <summary>Everything <c>render</c> touches outside its own files, so specs can hand it fakes.</summary>
internal sealed record RenderServices(
    IDockerInspector Docker,
    IResourceProbe Probe,
    HttpClient Http,
    TimeProvider Time,
    LoudnessMatch Match,
    string HostName)
{
    /// <summary>The real docker CLI, the real probes, the real clock and ffmpeg.</summary>
    internal static RenderServices Default() => new(
        new DockerCliInspector(),
        new ProcessResourceProbe(),
        SpeechClient.CreateHttpClient(),
        TimeProvider.System,
        new LoudnessMatch(),
        Environment.MachineName);
}
