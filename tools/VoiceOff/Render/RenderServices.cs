using GenWave.VoiceOff.Audio;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>Everything <c>render</c> touches outside its own files, so specs can hand it fakes.</summary>
internal sealed record RenderServices(
    IDockerInspector Docker,
    HttpClient Http,
    TimeProvider Time,
    LoudnessMatch Match)
{
    /// <summary>The real docker CLI, the real clock and ffmpeg.</summary>
    internal static RenderServices Default() => new(
        new DockerCliInspector(),
        SpeechClient.CreateHttpClient(),
        TimeProvider.System,
        new LoudnessMatch());
}
