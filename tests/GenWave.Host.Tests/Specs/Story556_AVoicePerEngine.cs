// STORY-556 — Every speaker keeps a voice per engine, settings and render half (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http.Json;
using GenWave.Tts;
using Microsoft.Extensions.Configuration;

public static class FeatureAVoicePerEngine
{
    static Task<HttpResponseMessage> PutAsync(HttpClient client, string key, string value) =>
        client.PutAsJsonAsync("/api/settings", new[] { new { key, value } });

    // AC3 — Station:Voice, AnnouncerVoice, CastVoices
    public sealed class ScenarioSettingsTakeBothShapes : EngineSettingScenario
    {
        HttpStatusCode bareId;
        string? resolvedBareId;
        HttpStatusCode engineMap;
        HttpStatusCode castList;
        HttpStatusCode announcerMap;
        HttpStatusCode legacyCastList;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            bareId = (await PutAsync(Client, "Station:Voice", "af_nova")).StatusCode;
            resolvedBareId = Service<IVoiceResolver>().Resolve(
                VoiceMap.TryParseSetting(Service<IConfiguration>()["Station:Voice"]) ?? VoiceMap.FromEngineVoices(new Dictionary<string, string>()));
            engineMap = (await PutAsync(Client, "Station:Voice", "{\"chatterbox\":\"reader-1\"}")).StatusCode;
            castList = (await PutAsync(Client, "Station:Ads:CastVoices",
                "[\"af_nova\",{\"kokoro\":\"am_michael\",\"chatterbox\":\"reader-2\"}]")).StatusCode;
            announcerMap = (await PutAsync(Client, "Station:Ads:AnnouncerVoice", "{\"kokoro\":\"af_nova\"}")).StatusCode;
            legacyCastList = (await PutAsync(Client, "Station:Ads:CastVoices", "af_nova,am_michael")).StatusCode;
        }

        [Fact]
        public void ABareIdValidates() => Assert.Equal(HttpStatusCode.OK, bareId);

        [Fact]
        public void AnEngineMapValidates() => Assert.Equal(HttpStatusCode.OK, engineMap);

        [Fact]
        public void ACastListPerEngineValidates() => Assert.Equal(HttpStatusCode.OK, castList);

        [Fact]
        public void AnAnnouncerEngineMapValidates() => Assert.Equal(HttpStatusCode.OK, announcerMap);

        [Fact]
        public void TheLegacyCommaListStillValidates() => Assert.Equal(HttpStatusCode.OK, legacyCastList);

        [Fact]
        public void ABareIdResolvesOnKokoro() => Assert.Equal("af_nova", resolvedBareId);
    }

    // AC4 — voices for both engines; kokoro → chatterbox → kokoro through the preview endpoint
    public sealed class ScenarioThereAndBack
    {
        [Fact(Skip = "pending: T753")]
        public void TheChatterboxRenderUsesTheChatterboxVoice() { }

        [Fact(Skip = "pending: T753")]
        public void TheLastRenderUsesTheFirstVoice() { }
    }

    // Sad path
    // AC7
    public sealed class ScenarioAnUnknownEngineKey : EngineSettingScenario
    {
        [Fact]
        public async Task TheSaveReturns400() =>
            Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(Client, "Station:Voice", "{\"nope\":\"x\"}")).StatusCode);
    }

    // Sad path
    public sealed class ScenarioABadVoiceShape : EngineSettingScenario
    {
        [Theory]
        [InlineData("Station:Voice", "{\"kokoro\":\"a\",\"Kokoro\":\"b\"}")]
        [InlineData("Station:Voice", "{\"kokoro\":\" \"}")]
        [InlineData("Station:Voice", "{}")]
        [InlineData("Station:Voice", "{\"kokoro\":5}")]
        [InlineData("Station:Ads:AnnouncerVoice", "{\"nope\":\"x\"}")]
        [InlineData("Station:Ads:CastVoices", "[{\"nope\":\"x\"}]")]
        [InlineData("Station:Ads:CastVoices", "[]")]
        [InlineData("Station:Ads:CastVoices", "[5]")]
        [InlineData("Station:Ads:CastVoices", "[{\"kokoro\":\"af_nova\"},{ \"kokoro\" : \"af_nova\" }]")]
        [InlineData("Station:Ads:CastVoices", "[{\"kokoro\":\"a\",\"chatterbox\":\"r\"},{\"chatterbox\":\"r\",\"kokoro\":\"b\"}]")]
        [InlineData("Station:Ads:CastVoices", "[\"af_nova\",{\"kokoro\":\"af_nova\"}]")]
        [InlineData("Station:Ads:CastVoices", "[{\"kokoro\":\"Bad Id!\"}]")]
        [InlineData("Station:Ads:AnnouncerVoice", "{\"kokoro\":\"Bad Id!\"}")]
        public async Task ItIsRefusedWith400(string key, string value) =>
            Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(Client, key, value)).StatusCode);
    }

    // Sad path
    // AC8 — a DJ whose map lacks the active engine
    public sealed class ScenarioNoVoiceForThisEngine
    {
        [Fact(Skip = "pending: T753")]
        public void TheTalkIsDropped() { }

        [Fact(Skip = "pending: T753")]
        public void AVoiceMissingRowNamesTheDj() { }

        [Fact(Skip = "pending: T753")]
        public void NoRenderRequestIsSent() { }
    }
}
