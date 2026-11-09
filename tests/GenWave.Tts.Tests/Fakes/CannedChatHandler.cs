using System.Net;
using System.Text;
using System.Text.Json;

namespace GenWave.Tts.Tests.Fakes;

/// <summary>
/// A fake chat endpoint for tools/PatterBench: answers POST /v1/chat/completions with a canned copy per call
/// and GET /api/ps with a canned body (404 when none), recording every chat request body.
/// </summary>
internal sealed class CannedChatHandler(Func<int, string> copyForCall, string? processListBody = null) : HttpMessageHandler
{
    public List<string> ChatBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v1/chat/completions")
        {
            ChatBodies.Add(request.Content is { } content ? await content.ReadAsStringAsync(cancellationToken) : string.Empty);
            var reply = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = copyForCall(ChatBodies.Count - 1) }, finish_reason = "stop" } },
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }

        if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/ps" && processListBody is not null)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(processListBody, Encoding.UTF8, "application/json") };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
