using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace GenWave.VoiceOff.Serving;

/// <summary>The guards every POST of <c>serve</c> shares: the body must be declared JSON (415) and must fit <see cref="ListeningRoutes.MaxBodyBytes"/> (413).</summary>
internal static class JsonBody
{
    private const string Json = "application/json";

    /// <returns>The body text, or null with <paramref name="refusal"/> set to the status to answer.</returns>
    internal static async Task<(string? Body, IResult? Refusal)> ReadAsync(HttpRequest request, CancellationToken ct)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out MediaTypeHeaderValue? type)
            || !string.Equals(type.MediaType, Json, StringComparison.OrdinalIgnoreCase))
        {
            return (null, Results.StatusCode(StatusCodes.Status415UnsupportedMediaType));
        }

        if (request.ContentLength > ListeningRoutes.MaxBodyBytes)
        {
            return (null, Results.StatusCode(StatusCodes.Status413PayloadTooLarge));
        }

        string? body = await BoundedBody.ReadAsync(request.Body, ListeningRoutes.MaxBodyBytes, ct);
        return body is null ? (null, Results.StatusCode(StatusCodes.Status413PayloadTooLarge)) : (body, null);
    }
}
