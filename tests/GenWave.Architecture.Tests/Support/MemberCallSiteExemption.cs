namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L7/L8's shared, unified exemption shape (T277 review: L7's per-type list and L8's per-(type,
/// member) list were two different shapes for the same idea, hand-checked by two different pieces of
/// code — one shape, one matcher, so one fixture probe and one resolution fact can serve both laws).
/// An allowed caller, named either at the TYPE granularity (<see cref="ForbiddenMember"/> left
/// <see langword="null"/> — "this type may call ANY of the law's forbidden signatures", L7's relay
/// shape: <see cref="GenWave.Tts.NormalizingTtsSynthesizer"/> and
/// <see cref="GenWave.Tts.FallbackTtsSynthesizer"/> each define the one forbidden overload their law
/// has, so a type-level exemption and a member-level one mean the same thing for them) or the
/// (type, member) granularity (<see cref="ForbiddenMember"/> set — "this type may call ONLY this one
/// forbidden member", L8's <c>PronunciationsController</c> shape: exempt for its display-purposed
/// <c>MergeWithProvenance</c> call, never for <c>Merge</c> or <c>BuildMerged</c>).
///
/// Named <see cref="ForbiddenMember"/>, not <c>Member</c>: this project already has an unrelated
/// <see cref="ArchitectureExemption.Member"/> (the offending TYPE/assembly a dated pre-existing-debt
/// entry names, L1/L2's shape) — a same-named property with a different meaning one namespace over
/// invites exactly the kind of mix-up a rename costs nothing to avoid.
/// </summary>
/// <param name="Type">The exempt caller's outermost declaring type, full name.</param>
/// <param name="ForbiddenMember">The one forbidden member (<see cref="ForbiddenMemberSignature.Description"/>
/// form) this exemption covers, or <see langword="null"/> to cover every forbidden member the owning
/// law's signature list names.</param>
internal sealed record MemberCallSiteExemption(string Type, string? ForbiddenMember = null)
{
    /// <summary>Whether a violation found at <paramref name="type"/> calling
    /// <paramref name="member"/> (a <see cref="ForbiddenMemberSignature.Description"/> string) is
    /// covered by this exemption.</summary>
    public bool Matches(string type, string member) =>
        Type == type && (ForbiddenMember is null || ForbiddenMember == member);
}
