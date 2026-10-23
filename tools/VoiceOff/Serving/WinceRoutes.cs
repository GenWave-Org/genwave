using System.Text.Json;
using GenWave.VoiceOff.Listening;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GenWave.VoiceOff.Serving;

/// <summary>
/// The wince routes of <c>serve</c> (SPEC F214.13): <c>GET /wince</c> lists the reels and the counts so far, <c>POST /wince</c> enters a count,
/// <c>GET /wince/&lt;reelId&gt;</c> plays a reel. Engine names appear in the list only once every reel has a count.
/// </summary>
internal static class WinceRoutes
{
    internal const int MaxCount = 999;

    internal static void Map(IEndpointRouteBuilder routes, ServeRun run, TimeProvider time)
    {
        routes.MapGet("/wince", (CancellationToken ct) => ListAsync(run, ct));
        routes.MapPost("/wince", (HttpContext http, CancellationToken ct) => EnterAsync(run, time, http.Request, ct));
        routes.MapGet("/wince/{reelId}", (string reelId) =>
            run.TryGetReelFile(reelId, out string? file) ? Results.Stream(File.OpenRead(file), "audio/wav", lastModified: null, enableRangeProcessing: true) : Results.NotFound());
    }

    private static async Task<IResult> ListAsync(ServeRun run, CancellationToken ct)
    {
        IReadOnlyDictionary<string, int> counts = await run.Winces.ReadCountsAsync(ct);
        bool allCounted = run.Reels.All(r => counts.ContainsKey(r.ReelId));
        IReadOnlyList<WinceReelReply> reels =
        [
            .. run.Reels.Select(r => new WinceReelReply(
                r.ReelId,
                counts.TryGetValue(r.ReelId, out int count) ? count : null,
                allCounted ? r.Engine : null)),
        ];
        return Results.Text(StrictJson.Write(new WinceReply(reels)), "application/json");
    }

    private static async Task<IResult> EnterAsync(ServeRun run, TimeProvider time, HttpRequest request, CancellationToken ct)
    {
        (string? body, IResult? refusal) = await JsonBody.ReadAsync(request, ct);
        if (body is null)
        {
            return refusal ?? Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        WinceRequest? wince = Parse(body);
        if (wince is null || wince.Count is < 0 or > MaxCount || !run.Reels.Any(r => string.Equals(r.ReelId, wince.ReelId, StringComparison.Ordinal)))
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        await run.Winces.AppendAsync(new WinceEntry(wince.ReelId, wince.Count, time.GetUtcNow()), ct);
        return Results.NoContent();
    }

    private static WinceRequest? Parse(string body)
    {
        try
        {
            return StrictJson.Read<WinceRequest>(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
