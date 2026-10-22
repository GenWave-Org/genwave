using System.Text.Json.Nodes;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Render;

public sealed class LanePlanTests
{
    private static EngineProfile Profile(string markup) =>
        EngineProfileReader.Parse(EngineProfileJson.With(n => n["markup"] = JsonNode.Parse(markup)), "alpha.json");

    [Fact]
    public void OneMappedCueAddsTheMarkupLane() =>
        Assert.Equal([RenderLane.Plain, RenderLane.Markup], LanePlan.For(Profile("""{ "pause": null, "laugh": "[laugh]", "sigh": null, "breath": null, "um": null, "emphasis": null }""")));

    [Fact]
    public void NoMappedCueIsPlainOnly() =>
        Assert.Equal([RenderLane.Plain], LanePlan.For(Profile("""{ "pause": null, "laugh": null, "sigh": null, "breath": null, "um": null, "emphasis": null }""")));
}
