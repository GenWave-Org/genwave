namespace GenWave.Core.Logging;

/// <summary>
/// Neutralizes user-controlled text before it reaches a log call (CodeQL <c>cs/log-forging</c>,
/// CWE-117): strips CR/LF so a crafted value cannot start a forged line in plain-text log output.
/// Structured sinks are already safe by construction — this exists for the console/text renderers.
/// </summary>
public static class LogSanitize
{
    public static string Strip(string? value) =>
        value is null ? string.Empty : value.Replace("\r", string.Empty).Replace("\n", string.Empty);

    /// <summary>
    /// For non-string values (e.g. filter records) logged via their <c>ToString()</c> rendering.
    /// </summary>
    public static string Strip(object? value) => Strip(value?.ToString());
}
