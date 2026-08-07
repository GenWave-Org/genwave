namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// The exemption mechanism's read side (F105.2): turns a raw list of <see cref="LawViolation"/>s
/// from any detector into "did anything NOT on the baseline fail" — built once at T211, reused by
/// every later law (T212–T214) regardless of which detector produced the violations.
/// </summary>
internal static class DependencyLawAssert
{
    /// <summary>Violations whose member is not named, for this law, in <paramref name="exemptions"/>.
    /// A baselined violation is silently dropped here; everything else survives to fail the caller's
    /// assertion — there is no other filtering path, so nothing is silently tolerated.</summary>
    public static IReadOnlyList<LawViolation> FindUnexempted(
        IEnumerable<LawViolation> violations, IReadOnlyList<ArchitectureExemption> exemptions) =>
        violations
            .Where(v => !exemptions.Any(e => e.LawId == v.LawId && e.Member == v.Member))
            .ToList();

    /// <summary>Fails with every unexempted violation's law id and offending member named in the
    /// message (STORY-290 AC5) unless <paramref name="violations"/> is empty once the baseline is
    /// applied.</summary>
    public static void AssertNone(
        IEnumerable<LawViolation> violations, IReadOnlyList<ArchitectureExemption> exemptions)
    {
        var unexempted = FindUnexempted(violations, exemptions);
        if (unexempted.Count > 0)
            Assert.Fail(Format(unexempted));
    }

    public static string Format(IReadOnlyList<LawViolation> violations) =>
        string.Join(Environment.NewLine, violations.Select(Format));

    public static string Format(LawViolation violation) =>
        $"[{violation.LawId}] {violation.Member}: {violation.Detail}";
}
