using System.Diagnostics;

namespace GenWave.Loudness;

/// <summary>
/// Drops an analyzer's child process to idle priority (gh-#38): an initial library enrichment
/// fans out ffmpeg/aubio measurement passes that were observed pinning the api container at
/// 240%+ CPU and starving the broadcast path's polling. Idle maps to <c>nice 19</c> on Linux, so
/// the measurement storm only ever consumes CPU nothing normal-priority wants — the engine,
/// Kokoro, Ollama, and the api's own request handling all outrank it.
/// <para>
/// Applied to every MEASUREMENT child (loudness, cue, energy, bpm decode, aubio) and deliberately
/// NOT to <see cref="FfmpegAudioMixer"/>'s children — that path assembles on-air audio and must
/// compete at normal priority. Best-effort by design: the child can exit before the priority
/// write lands, and a platform can refuse — a measurement's correctness never depends on its
/// scheduling class, so every failure here is swallowed.
/// </para>
/// </summary>
static class AnalyzerProcessPriority
{
    public static void TryLower(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.Idle;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
                or PlatformNotSupportedException
                or System.ComponentModel.Win32Exception)
        {
            // Child already exited, or the platform/permissions refused — proceed at whatever
            // priority the process inherited; the measurement itself is unaffected.
        }
    }
}
