namespace GenWave.Core.Domain;

/// <summary>
/// What the Status page's Repeats panel shows (SPEC F218.29): the DJs with repeat activity in the last 7
/// days, and the signature phrases said more than their per-shift limit in that window.
/// </summary>
public sealed record RepeatNumbers(IReadOnlyList<DjRepeatNumbers> Djs, IReadOnlyList<CatchphraseOveruse> Overused);
