using System.Net;
using System.Net.Http.Json;

namespace GenWave.Host.Tests.Support;

/// <summary>Posts to <c>/api/talk-rules/replay</c> and reads the body; fails loudly on a non-200 or an empty body.</summary>
internal static class ReplayClient
{
    public static async Task<ReplayBody> ReplayAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/talk-rules/replay", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<ReplayBody>() ?? throw new InvalidOperationException("The replay returned no body.");
    }
}
