using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;
using Xunit.Abstractions;

namespace GenWave.VoiceOff.Tests.Speech;

public sealed class DevStackKokoroSmoke(ITestOutputHelper output)
{
    /// <summary>The client reaches the dev stack's running baseline container through <c>docker inspect</c> and lists its voices.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task TheClientListsTheDevStacksVoices()
    {
        EngineProfile baseline = EngineProfileSet.Load(RealEngines.Directory).Baseline;
        using HttpClient http = SpeechClient.CreateHttpClient();

        ContainerGateResult gate = await ContainerGate.EvaluateAsync(baseline, new DockerCliInspector(), http, CancellationToken.None);

        output.WriteLine($"gate: {gate.Gate.Passed} {gate.Gate.Reason}");
        Assert.True(gate.Gate.Passed, gate.Gate.Reason);
    }
}
