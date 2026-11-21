using GenWave.Tts.Tests.Fakes;

namespace GenWave.Tts.Tests.Specs;

public sealed class ActiveEngineRenderBudgetProviderTests
{
    [Fact]
    public void AnUnregisteredActiveEngineFallsBackToTheTtsBudget()
    {
        var options = new TtsOptions { RenderBudgetSeconds = 45 };
        var provider = new ActiveEngineRenderBudgetProvider(new NoEngine(), new TestOptionsMonitor<TtsOptions>(options));

        Assert.Equal(TimeSpan.FromSeconds(45), provider.Current);
    }

    sealed class NoEngine : IActiveVoiceEngine
    {
        public string Name => "gone";
        public VoiceEngine? Current => null;
    }
}
