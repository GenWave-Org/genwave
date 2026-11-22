using Microsoft.Extensions.Options;

namespace GenWave.Tts;

/// <summary>Reads <c>Tts:SoundTags:MaxPerBreak</c> fresh from <see cref="IOptionsMonitor{T}"/> so a live settings edit reaches the next render.</summary>
public sealed class OptionsMonitorSoundTagCapProvider(IOptionsMonitor<TtsSoundTagsOptions> options) : ISoundTagCapProvider
{
    public int Current => Math.Max(0, options.CurrentValue.MaxPerBreak);
}
