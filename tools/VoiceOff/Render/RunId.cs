using System.Text.RegularExpressions;

namespace GenWave.VoiceOff.Render;

/// <summary>A run's folder name, chosen by whoever starts it: one path segment of letters, digits, dot, underscore and hyphen, starting with a letter or digit.</summary>
internal static partial class RunId
{
    internal const string Rule = "letters, digits, '.', '_' and '-', starting with a letter or digit, at most 64 characters";

    internal static bool IsValid(string? name) => name is not null && Shape().IsMatch(name);

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
