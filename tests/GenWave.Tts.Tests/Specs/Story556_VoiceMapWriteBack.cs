// STORY-556 — Every speaker keeps a voice per engine, the map round trip (SPEC F219.8 · PLAN T753b)
//
// A cast voice read out of a JSON array is written back as a setting value that parses to the same map, and
// two maps can be asked whether they would put the same voice on air.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureVoiceMapWriteBack
{
    static VoiceMap Parse(string value) => VoiceMap.TryParseSetting(value) ?? throw new InvalidOperationException(value);

    public sealed class ScenarioAMapIsWrittenBack
    {
        [Fact]
        public void AKokoroOnlyMapIsABareId() => Assert.Equal("af_nova", Parse("{\"kokoro\":\"af_nova\"}").ToSettingValue());

        [Fact]
        public void ABareIdStaysABareId() => Assert.Equal("af_nova", Parse("af_nova").ToSettingValue());

        [Fact]
        public void AMultiEngineMapIsJsonInEngineNameOrder() =>
            Assert.Equal(
                "{\"chatterbox\":\"nova\",\"kokoro\":\"af_nova\"}",
                Parse("{\"kokoro\":\"af_nova\",\"chatterbox\":\"nova\"}").ToSettingValue());

        [Fact]
        public void TheWrittenValueParsesBackToTheSameVoices()
        {
            var written = Parse("{\"kokoro\":\"af_nova\",\"chatterbox\":\"nova\"}").ToSettingValue();

            Assert.True(Parse(written).SharesVoiceWith(Parse("{\"chatterbox\":\"nova\"}")));
        }
    }

    public sealed class ScenarioTwoMapsAreCompared
    {
        [Fact]
        public void MapsSharingAVoiceOnOneEngineShareAVoice() =>
            Assert.True(Parse("{\"kokoro\":\"af_nova\",\"chatterbox\":\"a\"}").SharesVoiceWith(Parse("{\"kokoro\":\"af_nova\",\"chatterbox\":\"b\"}")));

        [Fact]
        public void MapsWithDifferentVoicesShareNothing() =>
            Assert.False(Parse("{\"kokoro\":\"af_nova\"}").SharesVoiceWith(Parse("{\"kokoro\":\"am_adam\"}")));

        [Fact]
        public void TheSameVoiceIdOnDifferentEnginesIsNotShared() =>
            Assert.False(Parse("{\"kokoro\":\"nova\"}").SharesVoiceWith(Parse("{\"chatterbox\":\"nova\"}")));
    }
}
