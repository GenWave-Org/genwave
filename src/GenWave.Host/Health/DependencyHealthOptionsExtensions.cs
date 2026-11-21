using GenWave.Host.Options;
using GenWave.Tts;

namespace GenWave.Host.Health;

static class DependencyHealthOptionsExtensions
{
    /// <summary>
    /// The probe cadence these options describe right now. The <c>Math.Max(1, ...)</c> floors only guard a hand-edited
    /// appsettings that bypasses <c>SettingValidator</c>: a zero interval would spin a loop hot. Callers re-read the
    /// options monitor per cycle, so a live edit lands on the next one.
    /// </summary>
    public static DependencyProbeCadence ToCadence(this DependencyHealthOptions options) => new(
        TimeSpan.FromSeconds(Math.Max(1, options.ProbeIntervalSeconds)),
        TimeSpan.FromSeconds(Math.Max(1, options.ProbeTimeoutSeconds)),
        Math.Max(1, options.UnhealthyThreshold));
}
