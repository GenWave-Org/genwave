// STORY-492 — Every clip is loudness-matched and anonymous (gh-#614 · SPEC F214.4, F214.9 · PLAN T609)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.

using System.Text.Json;
using GenWave.VoiceOff.Audio;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureEveryClipIsLoudnessMatchedAndAnonymous
{
    private static async Task<LoudnessMatchResult> MatchAsync(string input, string output) =>
        await new LoudnessMatch().MatchAsync(input, output, CancellationToken.None);

    private static JsonElement Stream(JsonDocument probe) => probe.RootElement.GetProperty("streams")[0];

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the level is matched
    public sealed class ScenarioTheLevelIsMatched : IAsyncLifetime
    {
        private readonly ClipFixtureDirectory clips = new();
        private LoudnessMatchResult result = LoudnessMatchResult.Failure("not run");
        private double measured;

        public async Task InitializeAsync()
        {
            string input = await clips.QuietSineAsync();
            string output = clips.PathFor("matched.wav");
            result = await MatchAsync(input, output);
            measured = (await new GenWave.Loudness.FfmpegLoudnessAnalyzer().AnalyzeAsync(output, CancellationToken.None)).IntegratedLufs;
        }

        public Task DisposeAsync()
        {
            clips.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void ItMeasures16LUFS1LU() => Assert.InRange(measured, -17.0, -15.0);

        [Fact]
        public void TheResultIsOk() => Assert.True(result.Ok);
    }

    // AC2 — the format is fixed
    public sealed class ScenarioTheFormatIsFixed : IAsyncLifetime
    {
        private readonly ClipFixtureDirectory clips = new();
        private JsonElement stream;
        private JsonDocument? probe;

        public async Task InitializeAsync()
        {
            string output = clips.PathFor("matched.wav");
            await MatchAsync(await clips.QuietSineAsync(), output);
            probe = await ClipFixtureDirectory.ProbeAsync(output);
            stream = Stream(probe);
        }

        public Task DisposeAsync()
        {
            probe?.Dispose();
            clips.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheSampleRateIs24KHz() => Assert.Equal("24000", stream.GetProperty("sample_rate").GetString());

        [Fact]
        public void TheClipIsMono() => Assert.Equal(1, stream.GetProperty("channels").GetInt32());

        [Fact]
        public void TheCodecIsPcmS16le() => Assert.Equal("pcm_s16le", stream.GetProperty("codec_name").GetString());
    }

    // AC3 — metadata is gone
    public sealed class ScenarioMetadataIsGone : IAsyncLifetime
    {
        private readonly ClipFixtureDirectory clips = new();
        private JsonDocument? probe;
        private byte[] bytes = [];
        private string inputBeforeMatch = "";

        public async Task InitializeAsync()
        {
            string input = await clips.QuietSineAsync("tagged.wav", "-metadata", "title=Secret", "-metadata", "artist=Nobody");
            inputBeforeMatch = System.Text.Encoding.Latin1.GetString(await File.ReadAllBytesAsync(input));
            string output = clips.PathFor("matched.wav");
            await MatchAsync(input, output);
            probe = await ClipFixtureDirectory.ProbeAsync(output);
            bytes = await File.ReadAllBytesAsync(output);
        }

        public Task DisposeAsync()
        {
            probe?.Dispose();
            clips.Dispose();
            return Task.CompletedTask;
        }

        private string Text => System.Text.Encoding.Latin1.GetString(bytes);

        [Fact]
        public void TheInputReallyCarriedTheTag() => Assert.Contains("Secret", inputBeforeMatch, StringComparison.Ordinal);

        [Fact]
        public void FfprobeShowsNoFormatTags() =>
            Assert.False(probe?.RootElement.GetProperty("format").TryGetProperty("tags", out _));

        [Fact]
        public void FfprobeShowsNoStreamTags() => Assert.False(Stream(probe ?? throw new InvalidOperationException()).TryGetProperty("tags", out _));

        [Fact]
        public void TheBytesCarryNoSecret() => Assert.DoesNotContain("Secret", Text, StringComparison.Ordinal);

        [Fact]
        public void TheBytesCarryNoInfoList() => Assert.DoesNotContain("LIST", Text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC4 — an unmeasurable clip fails
    public sealed class ScenarioAnUnmeasurableClipFails : IAsyncLifetime
    {
        private readonly ClipFixtureDirectory clips = new();
        private LoudnessMatchResult result = LoudnessMatchResult.Success(0);
        private string output = "";

        public async Task InitializeAsync()
        {
            string input = await clips.SilenceAsync("silent.wav", 0.2);
            output = clips.PathFor("matched.wav");
            result = await MatchAsync(input, output);
        }

        public Task DisposeAsync()
        {
            clips.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheResultIsOkFalse() => Assert.False(result.Ok);

        [Fact]
        public void TheReasonSaysItCouldNotBeMeasured() => Assert.Equal("loudness could not be measured", result.Error);

        [Fact]
        public void TheClipIsNeverPaired() => Assert.False(File.Exists(output));

        [Fact]
        public void NoTempFileIsLeftBehind() => Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(output) ?? "", "matched*"));
    }
}
