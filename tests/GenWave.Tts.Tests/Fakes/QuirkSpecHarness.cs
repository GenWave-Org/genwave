namespace GenWave.Tts.Tests.Fakes;

using System.Net;
using System.Text;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Shared arrangement for the Story533 quirk specs: an <see cref="LlmCopyWriter"/> wired to a fake
/// completions endpoint that hands the posted system prompt back to the spec.
/// </summary>
static class QuirkSpecHarness
{
    public const long PersonaId = 7;

    public static PersonaCard Card(string quirk, QuirkRule? rule = null) =>
        Card([quirk], rule is null ? null : [rule]);

    public static PersonaCard Card(IReadOnlyList<string> quirks, IReadOnlyList<QuirkRule>? rules) =>
        new(PersonaCard.CurrentSchemaVersion, "DJ Nova", "", "Soul line.", quirks,
            new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], [])
        {
            QuirkRules = rules,
        };

    public static SegmentRequest Request(string? shiftKeyText) =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station")
        {
            PersonaId = PersonaId,
            ShiftKeyText = shiftKeyText,
        };

    public static LlmCopyWriter WriterFor(
        PersonaCard card, IQuirkSelector selector, Func<string, Task>? onSystemPrompt = null, Action? onHttp = null)
    {
        var http = new SingleHandlerHttpClientFactory(new FakeHttpMessageHandler(async (req, _) =>
        {
            onHttp?.Invoke();
            if (req.Content is { } content && onSystemPrompt is not null)
            {
                var body = await content.ReadAsStringAsync();
                var prompt = JsonDocument.Parse(body).RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
                await onSystemPrompt(prompt ?? "");
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = "Coming up next a classic from the vault." } } } }),
                    Encoding.UTF8, "application/json"),
            };
        }));
        return new LlmCopyWriter(
            new TemplateCopyWriter(new PatterTemplateRenderer()), http,
            new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://fake-llm.local", Model = "m", TimeoutSeconds = 5, MaxCopyChars = 450 }),
            new LlmCopyStatusHolder(), new FakeActivePersonaAccessor { Card = card }, NullLogger<LlmCopyWriter>.Instance, TimeProvider.System,
            new LlmCallRecorder(
                new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader(), quirkSelector: selector);
    }

    /// <summary>An HTTP factory that answers every completion with <paramref name="reply"/> and appends each posted system prompt to <paramref name="prompts"/>.</summary>
    public static SingleHandlerHttpClientFactory CapturingHttp(List<string> prompts, string reply) =>
        new(new FakeHttpMessageHandler(async (req, _) =>
        {
            if (req.Content is { } content)
            {
                var body = await content.ReadAsStringAsync();
                prompts.Add(JsonDocument.Parse(body).RootElement.GetProperty("messages")[0].GetProperty("content").GetString() ?? "");
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = reply } } } }),
                    Encoding.UTF8, "application/json"),
            };
        }));

    /// <summary>Renders one break and returns the system prompt the LLM was sent.</summary>
    public static string PromptFor(PersonaCard card, IQuirkSelector selector, string? shiftKeyText)
    {
        string? prompt = null;
        WriterFor(card, selector, onSystemPrompt: p => { prompt = p; return Task.CompletedTask; })
            .WriteAsync(Request(shiftKeyText), CancellationToken.None).GetAwaiter().GetResult();
        return prompt ?? throw new InvalidOperationException("No completion request was captured");
    }
}
