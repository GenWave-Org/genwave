// STORY-097 — KokoroVoicesResponse deserializes both generations of the voices wire shape
//
// BDD specification — xUnit. kokoro-fastapi ≤ v0.2.x served GET /v1/audio/voices as
// { "voices": ["af_heart", ...] }; v0.6.0 serves { "voices": [{ "id": "af_heart",
// "name": "af_heart" }, ...] } (captured from the real ghcr.io/remsky/kokoro-fastapi-cpu:v0.6.0
// container during the image bump). Tts:Endpoint is operator-repointable at runtime (F36.4), so
// the client must flatten EITHER shape to the same id list rather than assume the pinned image's.

namespace GenWave.Tts.Tests.Specs;

using System.Text.Json;

public static class FeatureKokoroVoicesWireShapes
{
    static KokoroVoicesResponse Parse(string json)
    {
        var parsed = JsonSerializer.Deserialize<KokoroVoicesResponse>(json);
        Assert.NotNull(parsed);
        return parsed;
    }

    public sealed class ScenarioLegacyStringArrayShape
    {
        // The ≤ v0.2.x shape — the one KokoroStubServer and every shipped repoint spec emulate.
        readonly IReadOnlyList<string> ids =
            Parse("""{"voices":["af_heart","am_adam","zm_yunyang"]}""").VoiceIds();

        [Fact]
        public void YieldsEveryIdInOrder() =>
            Assert.Equal(["af_heart", "am_adam", "zm_yunyang"], ids);
    }

    public sealed class ScenarioObjectShape
    {
        // The v0.6.0 shape, verbatim from the real container's response.
        readonly IReadOnlyList<string> ids =
            Parse("""{"voices":[{"id":"af_alloy","name":"af_alloy"},{"id":"af_heart","name":"af_heart"}]}""")
                .VoiceIds();

        [Fact]
        public void YieldsTheIdOfEveryObjectInOrder() =>
            Assert.Equal(["af_alloy", "af_heart"], ids);
    }

    public sealed class ScenarioDegenerateEntries
    {
        [Fact]
        public void ANullVoicesPropertyYieldsAnEmptyList() =>
            Assert.Empty(Parse("""{"voices":null}""").VoiceIds());

        [Fact]
        public void AMissingVoicesPropertyYieldsAnEmptyList() =>
            Assert.Empty(Parse("""{}""").VoiceIds());

        [Fact]
        public void AnEntryOfAnUnrecognizedKindIsSkippedNotThrown() =>
            Assert.Equal(["af_heart"], Parse("""{"voices":[42,{"name":"no-id"},"af_heart"]}""").VoiceIds());

        [Fact]
        public void AnEmptyStringEntryIsSkipped() =>
            Assert.Empty(Parse("""{"voices":[""]}""").VoiceIds());
    }
}
