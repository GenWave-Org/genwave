using System.Text.RegularExpressions;

namespace GenWave.VoiceOff.Serving;

/// <summary>The judge name rule shared by every route: 1 to 32 letters, digits, spaces, underscores or hyphens, and something besides spaces.</summary>
internal static partial class JudgeName
{
    /// <summary>The name with the edge spaces trimmed (score trims the same way), or false when it breaks the rule.</summary>
    internal static bool TryNormalize(string? raw, out string judge)
    {
        judge = string.Empty;
        if (raw is null || !Shape().IsMatch(raw) || raw.Trim().Length == 0)
        {
            return false;
        }

        judge = raw.Trim();
        return true;
    }

    // \A and \z, not ^ and $: a trailing newline must not slip through.
    [GeneratedRegex(@"\A[A-Za-z0-9 _-]{1,32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
