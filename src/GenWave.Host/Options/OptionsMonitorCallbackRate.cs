using GenWave.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace GenWave.Host.Options;

/// <summary>Reads <see cref="MemoryCallbackOptions.CallbacksPerHour"/> fresh on every call, so a live settings edit applies at once.</summary>
sealed class OptionsMonitorCallbackRate(IOptionsMonitor<MemoryCallbackOptions> monitor) : ICallbackRate
{
    public int CallbacksPerHour => monitor.CurrentValue.CallbacksPerHour;
}
