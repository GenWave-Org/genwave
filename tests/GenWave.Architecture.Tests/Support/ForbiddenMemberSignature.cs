namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// One method call target a call-site law forbids (L7/L8's shared shape, <see cref="MemberCallSiteScan"/>'s
/// input): the declaring type by (namespace, name) plus the member's own name, and — only when that
/// name is overloaded and just ONE overload is forbidden — the exact parameter count that
/// disambiguates it from a sibling overload sharing the same name.
///
/// <see cref="GenWave.Core.Abstractions.ITtsSynthesizer"/> is the motivating case for
/// <see cref="ParameterCount"/>: its context-less <c>SynthesizeAsync(string, string,
/// CancellationToken)</c> (forbidden, 3 parameters) and its required, allowed context-aware
/// <c>SynthesizeAsync(TtsRenderContext, CancellationToken)</c> (2 parameters) share a name — a
/// name-only match would forbid the good overload too. L8's three targets
/// (<c>PronunciationRuleSet.Merge</c>, <c>PronunciationRuleSet.MergeWithProvenance</c>,
/// <c>PronunciationRuleProvider.BuildMerged</c>) each have exactly one overload in play, so
/// <see cref="ParameterCount"/> is <see langword="null"/> for all three — no arity check is needed to
/// tell them apart from anything else sharing their name.
/// </summary>
/// <param name="DeclaringNamespace">The forbidden member's declaring type's namespace, exactly as it
/// appears in a cross-assembly <c>TypeReference</c> row.</param>
/// <param name="DeclaringName">The forbidden member's declaring type's simple name.</param>
/// <param name="MemberName">The forbidden method's name.</param>
/// <param name="ParameterCount">The forbidden overload's parameter count, or <see langword="null"/>
/// when the name is not overloaded among the signatures the owning law cares about.</param>
/// <param name="Description">Human-readable "DeclaringType.Member" form used both as the violation
/// detail text and as the exact key a designated-exemption list matches against.</param>
internal sealed record ForbiddenMemberSignature(
    string DeclaringNamespace, string DeclaringName, string MemberName, int? ParameterCount, string Description);
