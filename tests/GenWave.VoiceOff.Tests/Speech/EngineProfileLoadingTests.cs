using System.Text.Json.Nodes;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Text;
using GenWave.VoiceOff.Tests.Support;
using Xunit.Abstractions;

namespace GenWave.VoiceOff.Tests.Speech;

public sealed class EngineProfileLoadingTests(ITestOutputHelper output)
{
    private static EngineProfile Load(string json, string fileName = "alpha.json") => EngineProfileReader.Parse(json, fileName);

    // Inserts the property right after the first "{" only, so the result is the valid profile plus one duplicate.
    private static string Dup(string json, string property)
    {
        int open = json.IndexOf('{', StringComparison.Ordinal);
        return json.Insert(open + 1, property + ",");
    }

    private sealed record Defect(string Name, Func<string> Json, string FieldToken, string FileName = "alpha.json");

    // Each row is a valid profile plus exactly one defect; FieldToken is what the refusal must name,
    // so a row can only pass when its own rule fired.
    private static readonly Defect[] Defects =
    [
        new("slug absent", () => EngineProfileJson.With(n => n.Remove("slug")), "slug"),
        new("slug differs from the file name", () => EngineProfileJson.With(n => n["slug"] = "other"), "must equal the file name"),
        new("slug has bad shape", () => EngineProfileJson.With(n => n["slug"] = "Bad_Slug", "Bad_Slug"), "lowercase", "Bad_Slug.json"),
        new("slug has a trailing newline", () => EngineProfileJson.With(n => n["slug"] = "alpha\n"), "lowercase", "alpha\n.json"),
        new("baseline is not a bool", () => EngineProfileJson.With(n => n["baseline"] = "yes"), "baseline"),
        new("container absent", () => EngineProfileJson.With(n => n.Remove("container")), "container"),
        new("container blank", () => EngineProfileJson.With(n => n["container"] = "  "), "container"),
        new("container looks like a flag", () => EngineProfileJson.With(n => n["container"] = "-rm"), "container"),
        new("port absent", () => EngineProfileJson.With(n => n.Remove("port")), "port"),
        new("port zero", () => EngineProfileJson.With(n => n["port"] = 0), "port"),
        new("port above 65535", () => EngineProfileJson.With(n => n["port"] = 65536), "port"),
        new("port is a string", () => EngineProfileJson.With(n => n["port"] = "8880"), "port"),
        new("endpoint is ftp", () => EngineProfileJson.With(n => n["endpoint"] = "ftp://host:1/"), "endpoint"),
        new("endpoint is relative", () => EngineProfileJson.With(n => n["endpoint"] = "/v1"), "endpoint"),
        new("tiers absent", () => EngineProfileJson.With(n => n.Remove("tiers")), "tiers"),
        new("tiers empty", () => EngineProfileJson.With(n => n["tiers"] = new JsonArray()), "tiers"),
        new("tier unknown", () => EngineProfileJson.With(n => n["tiers"] = new JsonArray("tpu")), "tier \"tpu\""),
        new("tier twice", () => EngineProfileJson.With(n => n["tiers"] = new JsonArray("cpu", "cpu")), "listed twice"),
        new("tier is a number", () => EngineProfileJson.With(n => n["tiers"] = new JsonArray(1)), "tiers"),
        new("voices absent", () => EngineProfileJson.With(n => n.Remove("voices")), "voices"),
        new("voices lack a register", () => EngineProfileJson.With(n => EngineProfileJson.Child(n, "voices").Remove("laconic")), "\"laconic\""),
        new("voices have an extra register", () => EngineProfileJson.With(n => EngineProfileJson.Child(n, "voices").Add("whisper", "v4")), "\"whisper\""),
        new("voice blank", () => EngineProfileJson.With(n => EngineProfileJson.Child(n, "voices")["bubbly"] = " "), "\"bubbly\""),
        new("voice is null", () => EngineProfileJson.With(n => EngineProfileJson.Child(n, "voices")["bubbly"] = null), "voices.bubbly"),
        new("voiceReason absent", () => EngineProfileJson.With(n => n.Remove("voiceReason")), "voiceReason"),
        new("voiceReason blank", () => EngineProfileJson.With(n => n["voiceReason"] = ""), "voiceReason"),
        new("markup absent", () => EngineProfileJson.With(n => n.Remove("markup")), "markup"),
        new("markup lacks a cue", () => EngineProfileJson.With(n => EngineProfileJson.Child(n, "markup").Remove("um")), "missing the cue \"um\""),
        new("markup has an unknown cue", () => EngineProfileJson.With(n => EngineProfileJson.Child(n, "markup").Add("sihg", null)), "unknown cue \"sihg\""),
        new("markup tag blank", () => EngineProfileJson.With(n => EngineProfileJson.Child(n, "markup")["laugh"] = " "), "\"laugh\""),
        new("speed zero", () => EngineProfileJson.With(n => n["speed"] = 0), "speed"),
        new("speed above 4", () => EngineProfileJson.With(n => n["speed"] = 4.1), "speed"),
        new("timeoutSeconds zero", () => EngineProfileJson.With(n => n["timeoutSeconds"] = 0), "timeoutSeconds"),
        new("timeoutSeconds above 600", () => EngineProfileJson.With(n => n["timeoutSeconds"] = 601), "timeoutSeconds"),
        new("license absent", () => EngineProfileJson.With(n => n.Remove("license")), "license"),
        new("license blank", () => EngineProfileJson.With(n => n["license"] = ""), "license"),
        new("notes is null", () => EngineProfileJson.With(n => n["notes"] = null), "$.notes"),
        new("unknown property", () => EngineProfileJson.With(n => n["extra"] = 1), "extra"),
        new("property in the wrong case", () => EngineProfileJson.With(n => { n.Remove("license"); n["License"] = "MIT"; }), "License"),
        new("duplicate property", () => Dup(EngineProfileJson.Valid().ToJsonString(), "\"port\": 1"), "not valid JSON"),
        new("not JSON", () => "this is not json", "not valid JSON"),
        new("JSON null", () => "null", "empty"),
        new("an array", () => "[]", "not valid JSON"),
    ];

    public static TheoryData<string> DefectNames() => new(Defects.Select(d => d.Name));

    [Theory]
    [MemberData(nameof(DefectNames))]
    public void EachDefectIsRefusedNamingTheFileAndTheField(string name)
    {
        Defect defect = Defects.Single(d => d.Name == name);
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => Load(defect.Json(), defect.FileName));
        output.WriteLine($"{name,-34} -> {ex.Message}");
        Assert.True(
            ex.Message.Contains(defect.FileName, StringComparison.Ordinal) && ex.Message.Contains(defect.FieldToken, StringComparison.Ordinal),
            $"The message should name '{defect.FileName}' and '{defect.FieldToken}': {ex.Message}");
    }

    [Fact]
    public void ThePlainValidProfileLoads() => Assert.Equal("alpha", Load(EngineProfileJson.Valid().ToJsonString()).Slug);

    [Fact]
    public void AbsentSpeedDefaultsToOne() => Assert.Equal(1.0, Load(EngineProfileJson.Valid().ToJsonString()).Speed);

    [Fact]
    public void AbsentTimeoutDefaultsTo120() => Assert.Equal(120, Load(EngineProfileJson.Valid().ToJsonString()).TimeoutSeconds);

    [Fact]
    public void AbsentBaselineMeansNotBaseline() =>
        Assert.False(Load(EngineProfileJson.With(n => n.Remove("baseline"))).Baseline);

    [Fact]
    public void ANullMarkupValueMeansStripTheCue() =>
        Assert.Null(Load(EngineProfileJson.Valid().ToJsonString()).Markup["pause"]);

    [Fact]
    public void ANativeTagIsKept() =>
        Assert.Equal("[laugh]", Load(EngineProfileJson.Valid().ToJsonString()).Markup["laugh"]);

    [Fact]
    public void TheMarkupMapNamesEveryKnownCue() =>
        Assert.Equal(
            CueVocabulary.Known.Order(StringComparer.Ordinal),
            Load(EngineProfileJson.Valid().ToJsonString()).Markup.Keys.Order(StringComparer.Ordinal));

    [Fact]
    public void AnEndpointOverrideIsKept() =>
        Assert.Equal("http://10.0.0.5:9000/", Load(EngineProfileJson.With(n => n["endpoint"] = "http://10.0.0.5:9000")).Endpoint?.AbsoluteUri);

    [Fact]
    public void ZeroBaselinesAreRefused()
    {
        using EngineDirectoryFixture directories = new();
        string directory = directories.Create(("alpha.json", EngineProfileJson.Valid("alpha", baseline: null).ToJsonString()));
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => EngineProfileSet.Load(directory));
        Assert.Matches("No engine profile .* has baseline true; exactly one must", ex.Message);
    }

    [Fact]
    public void TheDuplicateFixtureAddsExactlyOneProperty()
    {
        string valid = EngineProfileJson.Valid().ToJsonString();
        string dup = Dup(valid, "\"port\": 1");
        Assert.Equal(
            (valid.Count(c => c == '{'), valid.Split("\"port\"").Length + 1),
            (dup.Count(c => c == '{'), dup.Split("\"port\"").Length));
    }

    [Fact]
    public void EveryShippedProfileLoads() =>
        Assert.Equal(
            ["chatterbox", "cosyvoice", "csm", "dia", "f5", "fish", "higgs", "kokoro", "omnivoice", "orpheus", "parler", "piper", "qwen3", "spark", "styletts2", "vibevoice", "voxcpm"], EngineProfileSet.Load(RealEngines.Directory).Profiles.Select(p => p.Slug));

    [Fact]
    public void TheShippedBaselineGivesEachRegisterItsOwnVoice() =>
        Assert.Equal(3, RegisterKeys.All.Select(EngineProfileSet.Load(RealEngines.Directory).Baseline.VoiceFor).Distinct().Count());
}
