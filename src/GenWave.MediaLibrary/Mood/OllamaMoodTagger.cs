namespace GenWave.MediaLibrary.Mood;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Options;

/// <summary>
/// <see cref="IMoodTagger"/> over the configured LLM endpoint (SPEC F85.2-F85.4, STORY-216, T72):
/// the same OpenAI-compatible <c>POST /v1/chat/completions</c> shape
/// <c>GenWave.Tts.LlmCopyWriter</c> calls for on-air copy, reused honestly against the same
/// <c>Llm:Endpoint</c> — <see cref="MoodTaggerOptions"/>'s own remarks explain why this is a second
/// options class bound to the identical section rather than a cross-module reference.
///
/// The prompt is a constrained-output contract (F85.4): the system message names the closed
/// vocabulary and demands nothing else back; <see cref="MoodTagParser"/> then parses whatever prose
/// actually comes back (a small/local model rarely returns clean JSON) and filters it to that same
/// vocabulary — this class never trusts the model's shape, only <see cref="MoodTagParser"/>'s output.
///
/// No boot-frozen endpoint (the F36.2 precedent, mirrors <c>MusicBrainzYearLookup</c>): the endpoint,
/// model, and timeout are read from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> fresh on
/// every call. No single-flight gate here (contrast <c>LlmCopyWriter</c>) — the caller
/// (<c>EnrichmentService</c>'s mood-tag backfill) already serializes one row at a time, sequentially,
/// and only runs at all while <c>ILlmBatchGate</c> reports the LLM fully healthy (SPEC F85.3), so
/// there is never a concurrent on-air render to contend with from this side.
///
/// Never throws past this boundary (mirrors <see cref="GenWave.MediaLibrary.YearLookup.MusicBrainzYearLookup"/>):
/// any HTTP error, non-2xx status, malformed JSON, or the internal timeout firing all collapse to an
/// empty mood list — the legal "no confident tag" outcome (F85.4) — with <see cref="LastCallFailed"/>
/// (<see cref="IMoodTaggerDiagnostics"/>) distinguishing that endpoint-level failure from a genuine
/// zero-survivor miss.
/// </summary>
public sealed class OllamaMoodTagger(HttpClient http, IOptionsMonitor<MoodTaggerOptions> optionsMonitor)
    : IMoodTagger, IMoodTaggerDiagnostics
{
    /// <summary>
    /// Response-buffer ceiling for this typed client (mirrors <c>LlmCopyWriter.MaxResponseContentBytes</c>):
    /// a mood answer is a handful of words, never megabytes.
    /// </summary>
    public const long MaxResponseContentBytes = 1_048_576;

    static readonly string SystemPrompt =
        "You are a music mood tagger for a radio station's catalog. Given a track's title, artist, " +
        "and genre, respond with ONLY one to three words from this exact list, separated by commas, " +
        "and nothing else — no explanation, no punctuation beyond the commas: " +
        string.Join(", ", MoodVocabulary.Terms) + ".";

    /// <summary>See <see cref="IMoodTaggerDiagnostics"/>. Not thread-safe against concurrent
    /// <see cref="TagAsync"/> calls — safe under the production caller (the mood-tag backfill's
    /// sequential, one-row-at-a-time pacing), which never overlaps two calls to this instance.</summary>
    public bool LastCallFailed { get; private set; }

    public async Task<IReadOnlyList<string>> TagAsync(string? artist, string? title, string? genre, CancellationToken ct)
    {
        try
        {
            var cfg = optionsMonitor.CurrentValue;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSeconds));

            var requestUri = CombineEndpoint(cfg.Endpoint, "/v1/chat/completions");
            var body = new
            {
                model = cfg.Model,
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = BuildUserPrompt(artist, title, genre) },
                },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(body),
            };

            if (!string.IsNullOrEmpty(cfg.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);

            var response = await http.SendAsync(request, timeoutCts.Token);
            response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

            var payload = await response.Content.ReadFromJsonAsync<MoodChatCompletionResponse>(timeoutCts.Token);
            var text = payload?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;

            // A response was successfully received and parsed — this is a completed round trip,
            // regardless of whether any vocabulary term survives the parse (F85.4's miss/failure split).
            LastCallFailed = false;
            return MoodTagParser.Parse(text);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller cancelled (e.g. shutdown) — not our own TimeoutSeconds budget expiring, and
            // not an endpoint failure either. Propagate; LastCallFailed is deliberately left as-is.
            throw;
        }
        catch (Exception)
        {
            // Everything else lands here: our own timeout CTS firing, a non-2xx status
            // (EnsureSuccessStatusCode), a connect failure, malformed JSON. Every one of these is the
            // legal "no confident tag" outcome for the Core contract (F85.4) — never an exception past
            // the boundary — but IS an endpoint-level failure for the backfill's diagnostic.
            LastCallFailed = true;
            return [];
        }
    }

    static string BuildUserPrompt(string? artist, string? title, string? genre)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(title)) parts.Add($"Title: {title}");
        if (!string.IsNullOrWhiteSpace(artist)) parts.Add($"Artist: {artist}");
        if (!string.IsNullOrWhiteSpace(genre)) parts.Add($"Genre: {genre}");
        return string.Join(". ", parts);
    }

    /// <summary>
    /// Joins <paramref name="baseEndpoint"/> (which may itself carry a subpath, e.g.
    /// <c>https://host/openai</c>) with <paramref name="relativePath"/> without dropping that
    /// subpath — mirrors <c>GenWave.Tts.EndpointUri.Combine</c> (that helper is <c>internal</c> to a
    /// project this one must never reference), the same T3 review-finding fix applied here.
    /// </summary>
    static Uri CombineEndpoint(string baseEndpoint, string relativePath) =>
        new($"{baseEndpoint.TrimEnd('/')}/{relativePath.TrimStart('/')}");
}
