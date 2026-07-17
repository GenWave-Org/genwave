using System.ComponentModel.DataAnnotations;

namespace GenWave.MediaLibrary.Options;

/// <summary>
/// Configuration for scan availability grace (config section "Library:Scan", SPEC F58). Read fresh
/// per scan tick via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> — the same
/// F44.2 live-editable shape <see cref="LibraryOptions.ScanIntervalSeconds"/> already uses — so a
/// live PUT governs the very next tick's missing-diff with no api restart.
/// </summary>
public sealed class ScanOptions
{
    public const string Section = "Library:Scan";

    /// <summary>
    /// Consecutive scan ticks a known row's path must be absent from the directory listing before it
    /// flips ready→unavailable (F58.1). A tick that sees the path resets its counter to zero. 1
    /// reproduces the pre-F58 single-miss behavior. Documentation only — like
    /// <see cref="LibraryOptions"/>, this class is bound via plain <c>Configure&lt;T&gt;</c>, never
    /// <c>ValidateDataAnnotations()</c>, so the Host's <c>SettingValidator</c> (floor 1, ceiling 20)
    /// is the only place this range is actually enforced.
    /// </summary>
    [Range(1, 20)]
    public int MissThreshold { get; set; } = 2;
}
