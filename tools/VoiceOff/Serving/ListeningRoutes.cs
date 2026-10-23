using System.Net.Http.Headers;
using System.Text.Json;
using GenWave.VoiceOff.Listening;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GenWave.VoiceOff.Serving;

/// <summary>The routes of <c>serve</c>: <c>/</c>, <c>/clips/&lt;name&gt;</c>, <c>/session</c>, <c>/vote</c> and <c>/progress</c>. Nothing else is mapped, so everything else is a 404.</summary>
internal static class ListeningRoutes
{
    /// <summary>The largest vote body, in bytes.</summary>
    internal const int MaxBodyBytes = 4096;

    private const string Json = "application/json";

    internal static void Map(IEndpointRouteBuilder routes, ServeRun run, TimeProvider time)
    {
        routes.MapGet("/", () => Results.File(run.IndexPath, "text/html; charset=utf-8"));
        routes.MapGet("/clips/{name}", (string name) =>
            run.Book.TryGetClipFile(name, out string? file) ? Results.File(file, "audio/wav", enableRangeProcessing: true) : Results.NotFound());
        routes.MapGet("/session", (HttpContext http) => Session(run, http.Request));
        routes.MapGet("/progress", (HttpContext http, CancellationToken ct) => ProgressAsync(run, http.Request, ct));
        routes.MapPost("/vote", (HttpContext http, CancellationToken ct) => VoteAsync(run, time, http.Request, ct));
    }

    private static IResult Session(ServeRun run, HttpRequest request)
    {
        if (!Resolve(run, request, out string judge, out SessionEntry session, out IResult refusal))
        {
            return refusal;
        }

        return Results.Text(StrictJson.Write(new SessionReply(run.Book.CapFor(session), SessionShuffle.Order(session.Pairs, judge, session.Id))), Json);
    }

    private static async Task<IResult> ProgressAsync(ServeRun run, HttpRequest request, CancellationToken ct)
    {
        if (!Resolve(run, request, out string judge, out SessionEntry session, out IResult refusal))
        {
            return refusal;
        }

        Dictionary<string, Dictionary<string, VotePick>> votes = await run.Votes.ReadEffectiveAsync(ct);
        int done = session.Pairs.Select(p => p.PairId).Distinct(StringComparer.Ordinal)
            .Count(id => votes.TryGetValue(id, out Dictionary<string, VotePick>? picks) && picks.ContainsKey(judge));
        return Results.Text(StrictJson.Write(new ProgressReply(done, run.Book.CapFor(session))), Json);
    }

    private static async Task<IResult> VoteAsync(ServeRun run, TimeProvider time, HttpRequest request, CancellationToken ct)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out MediaTypeHeaderValue? type)
            || !string.Equals(type.MediaType, Json, StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (request.ContentLength > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        string? body = await BoundedBody.ReadAsync(request.Body, MaxBodyBytes, ct);
        if (body is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        VoteRequest? vote = Parse(body);
        if (vote is null || !JudgeName.TryNormalize(vote.Judge, out string judge) || !run.Book.HasPair(vote.PairId))
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        await run.Votes.AppendAsync(new Vote(vote.PairId, judge, vote.Pick, time.GetUtcNow(), vote.Why), ct);
        return Results.NoContent();
    }

    private static VoteRequest? Parse(string body)
    {
        try
        {
            return StrictJson.Read<VoteRequest>(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The judge and session of a GET: a bad judge is a 400, an unknown session is a 404.</summary>
    private static bool Resolve(ServeRun run, HttpRequest request, out string judge, out SessionEntry session, out IResult refusal)
    {
        judge = string.Empty;
        session = new SessionEntry(string.Empty, []);
        refusal = Results.Empty;
        if (!JudgeName.TryNormalize(request.Query["judge"].ToString(), out judge))
        {
            refusal = Results.StatusCode(StatusCodes.Status400BadRequest);
            return false;
        }

        if (!run.Book.TryGetSession(request.Query["s"].ToString(), out SessionEntry? found))
        {
            refusal = Results.NotFound();
            return false;
        }

        session = found;
        return true;
    }
}
