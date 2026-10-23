using System.Text.Json.Nodes;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Tests.Render;

public sealed class GatesFileTests
{
    private static JsonNode Json(params GateEntry[] entries) =>
        JsonNode.Parse(new GatesFile(entries).ToJson()) ?? throw new InvalidOperationException("empty");

    [Fact]
    public void AnAttemptedEngineCarriesBothGatesInCamelCase()
    {
        JsonNode engine = Json(new GateEntry("alpha", "MIT", true, null, GateResult.Pass("2 voices"), GateResult.Fail("one voice")))["engines"]?[0]
            ?? throw new InvalidOperationException("no engine");
        Assert.Equal(
            """{"slug":"alpha","license":"MIT","attempted":true,"notAttempted":null,"container":{"passed":true,"reason":"2 voices"},"voiceSelect":{"passed":false,"reason":"one voice"}}""",
            engine.ToJsonString());
    }

    [Fact]
    public void AnEngineNotAttemptedSaysWhyAndHasNoGates()
    {
        JsonNode engine = Json(new GateEntry("beta", "MIT", false, "tier cpu not in its tiers", null, null))["engines"]?[0]
            ?? throw new InvalidOperationException("no engine");
        Assert.Equal(
            """{"slug":"beta","license":"MIT","attempted":false,"notAttempted":"tier cpu not in its tiers","container":null,"voiceSelect":null}""",
            engine.ToJsonString());
    }

    [Fact]
    public void ItIsIndented() => Assert.Contains("\n  \"engines\"", new GatesFile([]).ToJson(), StringComparison.Ordinal);
}
