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

    /// <summary>
    /// Path roots exempt from the gh-#611 out-of-root quarantine: a scanned-library row living
    /// under one of these is DELIBERATELY outside <c>Library:MediaRoot</c> (today: authored
    /// safe/imaging assets, inserted directly and never discovered — SPEC F27.7) and must never be
    /// judged by the scan. Everything else outside the root is an unreachable ghost — a row a
    /// PREVIOUS root configuration discovered (the 2026-08-22 doubled-library incident: a host-side
    /// wire run catalogued the same files under host paths, and every pick of one died silently at
    /// the engine for seven days) — and is quarantined after <see cref="MissThreshold"/> consecutive
    /// scans, the same F58 grace that keeps a one-scan root misconfiguration from flipping a whole
    /// catalog. The default matches <c>Station:Safe:AuthoredRoot</c>'s own default; a deployment
    /// that relocates the authored volume must update both.
    /// </summary>
    public string[] QuarantineExemptRoots { get; set; } = ["/authored"];
}
