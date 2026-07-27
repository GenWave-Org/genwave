namespace GenWave.Host.Catalog;

/// <summary>
/// Neutralizes remote- or config-derived text before it enters a catalog log template (the
/// CodeQL log-forging class). Control characters — above all CR/LF, which would fabricate
/// whole log lines in the container→Alloy→Loki pipeline — become spaces, and the value is
/// capped so a hostile index cannot flood a WARN line. The one place truly arbitrary remote
/// bytes can reach a log is <c>CatalogIndexValidator</c>'s rejection reason (it quotes the
/// offending value verbatim, by design, on the path where validation has NOT passed); the
/// other catalog sites carry values already pinned to control-free shapes (\A..\z slugs,
/// hex-64 hashes) and pass through here as belt-and-braces, so the rule stays one sentence:
/// <b>every string in a catalog log line goes through <see cref="Sanitize"/>.</b>
/// </summary>
internal static class LogSafeText
{
    internal const int MaxLength = 200;

    internal static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var bounded = value.Length <= MaxLength ? value : value[..MaxLength] + "…";
        return string.Create(bounded.Length, bounded, static (chars, source) =>
        {
            for (var i = 0; i < chars.Length; i++)
            {
                var c = source[i];
                chars[i] = char.IsControl(c) ? ' ' : c;
            }
        });
    }
}
