using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Shared parse helper for re-enrichment field tokens (Epic J, STORY-051).
///
/// <para>
/// Both <c>POST /api/media/{id}/reenrich?fields=&lt;csv&gt;</c> and
/// <c>POST /api/media/bulk/reenrich</c> use this helper so the unknown-field→400 and
/// empty→All normalization rules are identical for both endpoints.
/// </para>
///
/// <para>
/// <b>Security:</b> only the seven documented token names (<c>cue</c>, <c>energy</c>,
/// <c>loudness</c>, <c>tags</c>, <c>bpm</c>, <c>year</c>, <c>all</c>) are accepted via an explicit
/// allowlist switch. Numeric strings (e.g. <c>"4"</c>, <c>"16"</c>), <c>"none"</c>, and any
/// other input are rejected — <c>TryParse</c> returns <c>false</c> and the caller
/// maps that to a 400 without touching the database.
/// </para>
/// </summary>
internal static class ReenrichFieldsParser
{
    /// <summary>
    /// Parses a comma-separated string of field tokens into a <see cref="ReenrichFields"/> bit-field.
    /// </summary>
    /// <param name="csv">Comma-separated token string, e.g. <c>"cue,energy"</c>. May be null or empty.</param>
    /// <param name="result">
    /// On success: the parsed flags, or <see cref="ReenrichFields.All"/> if the input is null/empty.
    /// On failure: <see cref="ReenrichFields.None"/>.
    /// </param>
    /// <returns>
    /// <c>true</c> when all tokens are recognized (or input is null/empty);
    /// <c>false</c> when any token is unrecognized (including numeric strings and "none") → caller should 400.
    /// </returns>
    public static bool TryParse(string? csv, out ReenrichFields result)
    {
        result = ReenrichFields.None;

        // AC5 normalization: missing or empty → All (reset all four groups in one transaction).
        if (string.IsNullOrWhiteSpace(csv))
        {
            result = ReenrichFields.All;
            return true;
        }

        var tokens = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            result = ReenrichFields.All;
            return true;
        }

        var combined = ReenrichFields.None;
        foreach (var token in tokens)
        {
            if (!TryParseToken(token, out var flag))
            {
                // Unknown token (including numeric strings, "none", etc.) → validation failure; caller returns 400.
                return false;
            }

            combined |= flag;
        }

        // A combined result of None cannot occur here (every token added at least one flag),
        // but guard defensively.
        result = combined == ReenrichFields.None ? ReenrichFields.All : combined;
        return true;
    }

    /// <summary>
    /// Parses a list of field token strings (from the bulk JSON body) into a
    /// <see cref="ReenrichFields"/> bit-field. Same rules as <see cref="TryParse(string?,out ReenrichFields)"/>:
    /// null/empty list → <see cref="ReenrichFields.All"/>; unknown token → returns <c>false</c>.
    /// </summary>
    public static bool TryParse(IReadOnlyList<string>? tokens, out ReenrichFields result)
    {
        if (tokens is null || tokens.Count == 0)
        {
            result = ReenrichFields.All;
            return true;
        }

        return TryParse(string.Join(",", tokens), out result);
    }

    /// <summary>
    /// Matches a single token against the seven documented allowlist names (case-insensitive).
    /// Numeric strings, "none", empty strings, and any other input return <c>false</c>.
    /// </summary>
    private static bool TryParseToken(string token, out ReenrichFields flag)
    {
        flag = token.ToLowerInvariant() switch
        {
            "cue"      => ReenrichFields.Cue,
            "energy"   => ReenrichFields.Energy,
            "loudness" => ReenrichFields.Loudness,
            "tags"     => ReenrichFields.Tags,
            "bpm"      => ReenrichFields.Bpm,
            "year"     => ReenrichFields.Year,
            "all"      => ReenrichFields.All,
            _          => ReenrichFields.None,
        };

        return flag != ReenrichFields.None;
    }
}
