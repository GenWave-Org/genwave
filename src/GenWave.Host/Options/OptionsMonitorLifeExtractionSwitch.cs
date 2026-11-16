using GenWave.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace GenWave.Host.Options;

/// <summary>Reads <see cref="MemoryLifeExtractionOptions.Enabled"/> fresh on every call, so a live settings edit applies at once.</summary>
sealed class OptionsMonitorLifeExtractionSwitch(IOptionsMonitor<MemoryLifeExtractionOptions> monitor) : ILifeExtractionSwitch
{
    public bool IsEnabled => monitor.CurrentValue.Enabled;
}
