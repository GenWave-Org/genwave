// STORY-558 — DJs can laugh, the render path (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit through the real host (WebApplicationFactory<Program>, real DI render chain): the engines'
// endpoints are one recording handler, so each fact reads the text an engine actually received.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.DependencyInjection;

public static partial class FeatureDjsCanLaughOnTheRenderPath
{
    const string TwoTags = "[laugh] Good evening, friends [sigh] and welcome.";

    // A sound tag is a bare [word]; a [pause:0.6s] or [word](/ipa/) markup token is not one.
    [GeneratedRegex(@"\[[A-Za-z][A-Za-z ]*\](?! ?\()")]
    private static partial Regex SoundTagRx();

    static int TagsReceived(string engineRequestBody) =>
        SoundTagRx().Matches(JsonDocument.Parse(engineRequestBody).RootElement.GetProperty("input").GetString() ?? "").Count;

    // a DJ line with two tags through the preview endpoint, cap 1
    public sealed class ScenarioAPreviewOnChatterbox : EngineSettingScenario
    {
        HttpStatusCode previewStatus;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            Assert.Equal(HttpStatusCode.OK, (await PutEngineAsync("chatterbox")).StatusCode);
            var preview = await Client.PostAsJsonAsync("/api/tts/preview", new { text = TwoTags, voice = "reader-1" });
            previewStatus = preview.StatusCode;
        }

        [Fact]
        public void ThePreviewRenders() => Assert.Equal(HttpStatusCode.OK, previewStatus);

        [Fact]
        public void TheEngineReceivesOneTag() => Assert.Equal(1, TagsReceived(Assert.Single(EngineRequestBodies)));
    }

    // Sad path
    // AC6, AC7
    public sealed class ScenarioWhereTagsNeverGo
    {
        public sealed class OnKokoro : EngineSettingScenario
        {
            HttpStatusCode previewStatus;

            public override async Task InitializeAsync()
            {
                await base.InitializeAsync();
                var preview = await Client.PostAsJsonAsync("/api/tts/preview", new { text = TwoTags, voice = "af_heart" });
                previewStatus = preview.StatusCode;
            }

            [Fact]
            public void ThePreviewRenders() => Assert.Equal(HttpStatusCode.OK, previewStatus);

            [Fact]
            public void TheKokoroEngineReceivesNoTag() => Assert.Equal(0, TagsReceived(Assert.Single(EngineRequestBodies)));
        }

        // The ad render chain's own context kind, on the engine that does take tags
        public sealed class AnAdOnChatterbox : EngineSettingScenario
        {
            public override async Task InitializeAsync()
            {
                await base.InitializeAsync();
                Assert.Equal(HttpStatusCode.OK, (await PutEngineAsync("chatterbox")).StatusCode);
                var context = new TtsRenderContext(TwoTags, "reader-1", SegmentKind.Ad) { IsAudition = true };
                await Service<ITtsSynthesizer>().SynthesizeAsync(context, CancellationToken.None);
            }

            [Fact]
            public void AnAdRenderInputHasNoTags() => Assert.Equal(0, TagsReceived(Assert.Single(EngineRequestBodies)));
        }
    }
}
