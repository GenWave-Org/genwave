using GenWave.VoiceOff.Text;

namespace GenWave.VoiceOff.Tests.Text;

public sealed class ScriptFileTests
{
    [Fact]
    public void AScriptWithOutOfOrderParagraphsIsRefused()
    {
        const string Json = """{ "version": "v2", "paragraphs": [ { "n": 2, "text": "Hi." } ] }""";

        Assert.Throws<InvalidDataException>(() => ScriptFile.Parse(Json, "script.json"));
    }

    [Fact]
    public void AScriptWithAnUnknownMemberIsRefused()
    {
        const string Json = """{ "version": "v2", "extra": 1, "paragraphs": [ { "n": 1, "text": "Hi." } ] }""";

        Assert.Throws<InvalidDataException>(() => ScriptFile.Parse(Json, "script.json"));
    }
}
