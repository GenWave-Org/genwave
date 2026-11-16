namespace GenWave.Tts;

/// <summary>
/// Strips a leading <c>&lt;persona name&gt;:</c> speaker label (optionally markdown-bold) from single-voice
/// LLM copy (PLAN T723, STORY-534: two Big Tony lines aired as <c>Big Tony: "Ladies and gents…</c>).
/// Only the speaking persona's own name, only at the very start; the quote pair the label brings is
/// unwrapped afterwards by the ordinary hygiene pass. Crosstalk scripts never call this — labels are
/// their format.
/// </summary>
internal static class SpeakerLabel
{
    public static string Strip(string raw, string? personaName)
    {
        var name = personaName?.Trim();
        if (string.IsNullOrEmpty(name))
            return raw;

        var text = raw.TrimStart();
        var bold = text.StartsWith("**", StringComparison.Ordinal);
        var rest = bold ? text[2..].TrimStart() : text;
        if (!rest.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            return raw;

        rest = rest[name.Length..];
        if (bold)
        {
            // **Name:** or **Name**:
            if (!rest.StartsWith(":**", StringComparison.Ordinal) && !rest.StartsWith("**:", StringComparison.Ordinal))
                return raw;
            rest = rest[3..];
        }
        else
        {
            rest = rest.TrimStart();
            if (!rest.StartsWith(':'))
                return raw;
            rest = rest[1..];
        }

        return rest.TrimStart();
    }
}
