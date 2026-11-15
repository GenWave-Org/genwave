using System.Text.Json;
using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>
/// Reads the extractor model's reply, the untrusted boundary of the life extractor (SPEC F218.17): a JSON array
/// of <c>{claim, about, category}</c>. Anything that is not that shape is "not JSON" and yields nothing.
/// <c>category</c> is asked for to steer the model but a life row has nowhere to keep it, so it is not read.
/// </summary>
public static class LifeClaimParser
{
    /// <summary>Longest claim kept; a longer one is prose, not a fact.</summary>
    public const int MaxClaimChars = MemoryText.MaxChars;

    /// <summary>Most claims taken from one reply.</summary>
    public const int MaxClaims = 5;

    /// <summary>
    /// The claims in <paramref name="reply"/>, or null when it is not a JSON array. Elements without a usable
    /// <c>claim</c> are skipped; a Markdown code fence around the array is tolerated.
    /// </summary>
    public static IReadOnlyList<ExtractedClaim>? Parse(string reply)
    {
        try
        {
            using var document = JsonDocument.Parse(StripFence(reply));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            return document.RootElement.EnumerateArray()
                .Select(ClaimOf)
                .OfType<ExtractedClaim>()
                .Take(MaxClaims)
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static ExtractedClaim? ClaimOf(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("claim", out var claim) || claim.ValueKind != JsonValueKind.String)
            return null;

        var text = claim.GetString()?.ReplaceLineEndings(" ").Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxClaimChars)
            return null;

        var about = element.TryGetProperty("about", out var who) && who.ValueKind == JsonValueKind.String
            ? who.GetString()?.Trim() ?? string.Empty
            : string.Empty;
        return new ExtractedClaim(text, about);
    }

    static string StripFence(string reply)
    {
        var text = reply.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;

        var firstLineEnd = text.IndexOf('\n', StringComparison.Ordinal);
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd < 0 || lastFence <= firstLineEnd ? text : text[(firstLineEnd + 1)..lastFence].Trim();
    }
}
