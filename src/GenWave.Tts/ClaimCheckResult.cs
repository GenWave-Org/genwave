namespace GenWave.Tts;

/// <summary>
/// The verdict <see cref="CopyClaims.CheckFacts"/>/<see cref="CopyClaims.CheckClock"/> hand back
/// (SPEC F138.1): zero or more <see cref="ClaimViolation"/>s, never a bare pass/fail bool alone — the
/// F138.4 ladder's re-ask prompt needs to NAME each violation, not just know one exists.
/// <see cref="Passed"/> is a computed convenience (<c>Violations.Count == 0</c>), never an
/// independently-settable field a caller could desync from the list it describes.
///
/// <para>
/// <b>Record equality note (no consumer relies on this today, T329 review round 3):</b> the compiler-
/// generated <c>Equals</c>/<c>GetHashCode</c> this record derives from its positional
/// <see cref="Violations"/> parameter compare that property by REFERENCE, not by sequence content —
/// <see cref="IReadOnlyList{T}"/> has no structural equality of its own, so two results built from
/// separately-allocated-but-identical violation lists are NOT <c>Equals</c>-equal. If a future
/// consumer (T331/T332's ladder, a test) ever needs "same violations" comparison, it needs its own
/// sequence comparison (e.g. <see cref="Enumerable.SequenceEqual{TSource}(IEnumerable{TSource}?,IEnumerable{TSource}?)"/>),
/// not this record's own <c>==</c>.
/// </para>
/// </summary>
public sealed record ClaimCheckResult(IReadOnlyList<ClaimViolation> Violations)
{
    /// <summary>True when <see cref="Violations"/> is empty — the copy airs as written.</summary>
    public bool Passed => Violations.Count == 0;
}
