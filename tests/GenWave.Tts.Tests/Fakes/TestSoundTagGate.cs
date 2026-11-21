namespace GenWave.Tts.Tests.Fakes;

/// <summary>The real <see cref="SoundTagRenderGate"/> over a named active engine and a fixed cap, for specs that build a render chain by hand.</summary>
public static class TestSoundTagGate
{
    public static SoundTagRenderGate On(string engine, int cap = 1) =>
        new(TestActiveEngine.Named(engine),
            new OptionsMonitorSoundTagCapProvider(new TestOptionsMonitor<TtsSoundTagsOptions>(new TtsSoundTagsOptions { MaxPerBreak = cap })));

    /// <summary>A station on kokoro: no tag reaches a render.</summary>
    public static SoundTagRenderGate Kokoro => On("kokoro");
}
