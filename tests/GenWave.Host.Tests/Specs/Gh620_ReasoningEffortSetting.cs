// gh-#620 — Thinking-capable models return empty copy: the Llm:ReasoningEffort setting
//
// BDD specification — xUnit. The setting facts follow Story250_AudiencePostureSetting's own
// shape (seeded default, allowlist entry, validator guard); the wire fact drives the real
// LlmWishParser against a scripted FakeHttpMessageHandler (Story225's idiom) — the one poster
// that lives in GenWave.Host. settings-help-keys.ts parity is covered by Story151's existing
// FeatureSettingsHelpKeysParity fact the moment the key joins the allowlist.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Llm;
using GenWave.Host.Configuration;
using GenWave.Host.Requests;
using GenWave.Host.Tests.Fakes;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

public static class FeatureReasoningEffortSetting
{
    const string Key = "Llm:ReasoningEffort";

    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string AppSettingsPath =>
        Path.Combine(RepoRoot, "src", "GenWave.Host", "appsettings.json");

    static SettingValidator BuildValidator() =>
        new(new ConfigurationBuilder().Build());

    // ---------------------------------------------------------------------
    // HAPPY PATH — the setting exists, seeded, allowlisted, validated
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheSettingExists
    {
        [Fact]
        public void The_seeded_default_is_none_and_matches_the_options_default()
        {
            // F55.1/Story151 seeded-defaults discipline: the C# default must surface as a real
            // configured value (the gitea-#231 root cause), and it must be the posture that fixes #620.
            var config = new ConfigurationBuilder().AddJsonFile(AppSettingsPath, optional: false).Build();

            Assert.Equal(new LlmOptions().ReasoningEffort, config[Key]);
            Assert.Equal(ReasoningEffort.None, config[Key]);
        }

        [Fact]
        public void The_key_is_allowlisted_as_a_live_choice()
        {
            var allowed = Assert.Contains(Key, StationSettingsAllowlist.ByKey);

            Assert.Equal(SettingApplyMode.Live, allowed.ApplyMode);
            Assert.Equal(SettingKind.Choice, allowed.Kind);
        }

        [Fact]
        public void The_choices_are_exactly_the_vocabulary_in_order()
        {
            var allowed = StationSettingsAllowlist.ByKey[Key];

            Assert.NotNull(allowed.Choices);
            Assert.Equal(ReasoningEffort.Accepted, allowed.Choices.Select(choice => choice.Value));
        }

        [Fact]
        public void None_is_the_one_default_choice()
        {
            var allowed = StationSettingsAllowlist.ByKey[Key];

            Assert.Equal(ReasoningEffort.None, Assert.Single(allowed.Choices!, choice => choice.IsDefault).Value);
        }

        [Theory]
        [InlineData("none")]
        [InlineData("low")]
        [InlineData("medium")]
        [InlineData("high")]
        [InlineData("omit")]
        [InlineData("NONE")]
        [InlineData("Omit")]
        public void Every_vocabulary_value_is_accepted_case_insensitively(string value)
        {
            Assert.Null(BuildValidator().Validate(Key, value));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the validator's door
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheValidatorRefuses
    {
        [Theory]
        [InlineData("")]
        [InlineData("maximum")]
        [InlineData("none,low")]
        [InlineData("1")]
        public void Anything_outside_the_vocabulary_is_refused(string value)
        {
            Assert.NotNull(BuildValidator().Validate(Key, value));
        }

        [Fact]
        public void The_refusal_names_the_vocabulary()
        {
            var message = BuildValidator().Validate(Key, "maximum");

            Assert.NotNull(message);
            Assert.Contains("none, low, medium, high, omit", message);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the wish parser's request (the Host-side poster)
    // ---------------------------------------------------------------------

    public sealed class ScenarioWishParserOnTheWire
    {
        static HttpResponseMessage ChatResponse(string content) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { choices = new[] { new { message = new { content } } } }),
        };

        static async Task<string> CapturedBodyAsync(string reasoningEffort)
        {
            string? capturedBody = null;
            var handler = new FakeHttpMessageHandler(async (request, ct) =>
            {
                capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
                return ChatResponse("{\"artist\":null,\"title\":null,\"moods\":[]}");
            });
            var parser = new LlmWishParser(
                new SingleHandlerHttpClientFactory(handler),
                new FakeOptionsMonitor<LlmOptions>(new LlmOptions
                {
                    Endpoint = "https://llm.example/v1", Model = "test-model", ReasoningEffort = reasoningEffort,
                }),
                new DeterministicWishParser(),
                NullLogger<LlmWishParser>.Instance);

            await parser.ParseAsync("something dreamy", [], CancellationToken.None);

            Assert.NotNull(capturedBody);
            return capturedBody;
        }

        [Fact]
        public async Task The_default_request_carries_reasoning_effort_none()
        {
            using var doc = JsonDocument.Parse(await CapturedBodyAsync(new LlmOptions().ReasoningEffort));

            Assert.Equal("none", doc.RootElement.GetProperty("reasoning_effort").GetString());
        }

        [Fact]
        public async Task Omit_leaves_the_field_out_of_the_request_entirely()
        {
            using var doc = JsonDocument.Parse(await CapturedBodyAsync("omit"));

            Assert.False(doc.RootElement.TryGetProperty("reasoning_effort", out _));
        }
    }
}
