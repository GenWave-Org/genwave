namespace GenWave.Architecture.Tests.Support;

/// <summary>How a <c>Skip</c> reason classifies under the skip-prefix law (SPEC F182, STORY-443).</summary>
internal enum SkipClass
{
    /// <summary><c>pending: T&lt;n&gt;</c> — a planned task turns it green.</summary>
    Pending,
    /// <summary><c>manual:</c> — evidence only a person can produce (the LLL ear).</summary>
    Manual,
    /// <summary><c>gate:</c> — covered by a <c>stack_gate.sh</c> assertion; deleted by T507.</summary>
    Gate,
    /// <summary><c>obsolete:</c> — the task or behaviour is gone; deleted by T507.</summary>
    Obsolete,
    /// <summary>None of the four prefixes.</summary>
    Violation,
}

/// <summary>One skipped fact as the source scan sees it.</summary>
internal sealed record SkipReason(string File, string Fact, string Reason, SkipClass Class);

/// <summary>
/// Source scan behind the skip-prefix law (SPEC F182, STORY-443, PLAN T505): every
/// <c>[Fact(Skip = …)]</c> under <c>tests/</c>, with a const identifier resolved through the same
/// file's <c>const string</c> declarations, classified by <see cref="Classify"/>.
/// </summary>
/// <remarks>Skeleton at plan time — throws until T505 lands.</remarks>
internal static class SkipReasons
{
    public static SkipClass Classify(string reason) =>
        throw new NotImplementedException("pending: T505 — SkipReasons.Classify (STORY-443)");

    /// <summary>Every skipped fact under <paramref name="root"/> (recursive, <c>*.cs</c>).</summary>
    public static IReadOnlyList<SkipReason> Scan(string root) =>
        throw new NotImplementedException("pending: T505 — SkipReasons.Scan (STORY-443)");
}
