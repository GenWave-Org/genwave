using System.Text.Json;
using GenWave.VoiceOff.Listening;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GenWave.VoiceOff.Serving;

/// <summary>
/// The routes of <c>serve</c> (SPEC F217.7): <c>GET /</c>, <c>GET /state</c>, <c>GET /clips/&lt;cardId&gt;.wav</c> and <c>PUT /judgement</c>.
/// Nothing else is mapped, so everything else is a 404.
/// </summary>
internal static class ServeRoutes
{
    private const string Text = "text/plain; charset=utf-8";

    internal static void Map(IEndpointRouteBuilder routes, ServeRun run)
    {
        string page = ListeningPage.Html();
        routes.MapGet("/", () => Results.Text(page, "text/html; charset=utf-8"));
        routes.MapGet("/state", (CancellationToken ct) => StateAsync(run, ct));
        routes.MapGet("/clips/{file}", (string file) => Clip(run, file));
        routes.MapPut("/judgement", (HttpRequest request, CancellationToken ct) => SaveAsync(run, request, ct));
    }

    private static async Task<IResult> StateAsync(ServeRun run, CancellationToken ct)
    {
        // A judgement.json that went bad mid-serve throws; the host's 500 answer names no internals and logs the cause.
        return Results.Text(StrictJson.Write(StateReply.Build(run, await run.Judgements.ReadAsync(ct))), JsonBody.Json);
    }

    // "<cardId>.wav" where the id is a card id and a card of the run that rendered; the path comes from the run, not from the request.
    private static IResult Clip(ServeRun run, string file)
    {
        const string Extension = ".wav";
        return file.EndsWith(Extension, StringComparison.Ordinal)
            && run.TryGetClip(file[..^Extension.Length], out string? path)
                ? Results.File(path, "audio/wav", enableRangeProcessing: true)
                : Results.NotFound();
    }

    private static async Task<IResult> SaveAsync(ServeRun run, HttpRequest request, CancellationToken ct)
    {
        (string? body, IResult? refusal) = await JsonBody.ReadAsync(request, ct);
        if (body is null)
        {
            return refusal ?? Refuse("The body is missing.");
        }

        JudgementRequest judgement;
        try
        {
            judgement = StrictJson.Read<JudgementRequest>(body);
        }
        catch (JsonException ex)
        {
            return Refuse($"The body is not a judgement at {ex.Path ?? "$"}.");
        }

        string? problem = JudgementCheck.Problem(judgement, run);
        return problem is not null
            ? Refuse(problem)
            : Results.Text(StrictJson.Write(await run.Judgements.SaveAsync(judgement, ct)), JsonBody.Json);
    }

    private static IResult Refuse(string message) => Results.Text(message, Text, statusCode: StatusCodes.Status400BadRequest);
}
