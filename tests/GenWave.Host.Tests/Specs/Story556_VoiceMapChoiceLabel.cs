// STORY-556 — Every speaker keeps a voice per engine, the settings list (SPEC F219.8 · PLAN T753b)
//
// A saved voice that is an engine map is not "not found" just because no installed voice carries that
// exact text; the settings list shows it plainly.

namespace GenWave.Host.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Tts;
using Microsoft.Extensions.Time.Testing;
using GenWave.Host.Theming;
using Microsoft.Extensions.Logging.Abstractions;

public static class FeatureVoiceMapChoiceLabel
{
    sealed class OneVoiceProbe : IChoiceProbe
    {
        public string Name => TtsVoiceChoiceProbe.ProbeName;

        public string ScopeKey() => "test";

        public Task<IReadOnlyList<SettingChoice>> FetchAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SettingChoice>>([new SettingChoice("af_bella", "af_bella")]);
    }

    static async Task<IReadOnlyList<SettingChoice>> ChoicesForAsync(string saved, string key = "Station:Voice")
    {
        var resolver = new SettingChoiceResolver(
            new FakeIconPackStore(), ThemeCatalog.LoadShipped(), TestSettingCopy.Real(), [new OneVoiceProbe()], [],
            new ProbedChoiceCache(new FakeTimeProvider(), NullLogger<ProbedChoiceCache>.Instance),
            NullLogger<SettingChoiceResolver>.Instance);
        var resolved = await resolver.ResolveAsync(
            new Dictionary<string, string> { [key] = saved }, CancellationToken.None);
        return resolved[key].Choices;
    }

    public sealed class ScenarioASavedEngineMap
    {
        [Fact]
        public async Task IsListedAsOneVoicePerEngine() =>
            Assert.Equal(
                "One voice per engine (chatterbox: nova, kokoro: af_nova)",
                (await ChoicesForAsync("{\"kokoro\":\"af_nova\",\"chatterbox\":\"nova\"}"))[^1].Label);

        [Fact]
        public async Task KeepsTheSavedTextAsItsValue() =>
            Assert.Equal(
                "{\"kokoro\":\"af_nova\",\"chatterbox\":\"nova\"}",
                (await ChoicesForAsync("{\"kokoro\":\"af_nova\",\"chatterbox\":\"nova\"}"))[^1].Value);
    }

    public sealed class ScenarioASavedPlainVoiceMissingFromTheList
    {
        [Fact]
        public async Task StillSaysNotFound() =>
            Assert.EndsWith("(not found)", (await ChoicesForAsync("af_gone"))[^1].Label, StringComparison.Ordinal);
    }

    public sealed class ScenarioAJsonObjectSavedInANonVoiceSetting
    {
        [Fact]
        public async Task StillSaysNotFound() =>
            Assert.EndsWith(
                "(not found)", (await ChoicesForAsync("{\"kokoro\":\"af_nova\"}", "Station:Theme"))[^1].Label,
                StringComparison.Ordinal);
    }
}
