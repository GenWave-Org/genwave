// T759 facts for the live Tts:SoundTags:MaxPerBreak reader (SPEC F219.16/.17).

namespace GenWave.Tts.Tests.Specs;

using GenWave.Tts.Tests.Fakes;

public sealed class SoundTagCapProviderTests
{
    readonly TestOptionsMonitor<TtsSoundTagsOptions> monitor = new(new TtsSoundTagsOptions { MaxPerBreak = 1 });
    readonly OptionsMonitorSoundTagCapProvider provider;

    public SoundTagCapProviderTests() => provider = new OptionsMonitorSoundTagCapProvider(monitor);

    [Fact]
    public void ItReadsAChangedValueOnTheNextCall()
    {
        _ = provider.Current;
        monitor.CurrentValue = new TtsSoundTagsOptions { MaxPerBreak = 3 };

        Assert.Equal(3, provider.Current);
    }

    [Fact]
    public void ANegativeValueIsZero()
    {
        monitor.CurrentValue = new TtsSoundTagsOptions { MaxPerBreak = -2 };

        Assert.Equal(0, provider.Current);
    }
}
