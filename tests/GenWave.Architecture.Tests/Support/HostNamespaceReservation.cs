namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// One entry in the L5 tripwire's reserved-namespace list (SPEC F105.4): a namespace <c>GenWave.Host</c>
/// may never contain a type under, because that subsystem is either born outside Host from the start
/// (today's seed: Context, Ads) or has since graduated out of Host and must not creep back in
/// (the list's second, still-empty category — the graduation ladder in ARCHITECTURE.md "The Host
/// graduation rule"). One flat list serves both categories: a graduation is "one line" precisely
/// because it is just another entry here, not a second list to remember to update.
/// </summary>
/// <param name="ReservedNamespace">The reserved namespace itself — matched exactly, or as the prefix a
/// nested namespace must start with (e.g. <c>GenWave.Host.Context</c> also reserves
/// <c>GenWave.Host.Context.Anything</c>, but never a same-prefix lookalike like
/// <c>GenWave.Host.ContextLike</c>).</param>
/// <param name="RulingReference">The SPEC clause that ruled this reservation (e.g. <c>"F105.4"</c>) —
/// named separately from <see cref="Reason"/> so a violation message can cite it precisely, the AC2
/// literal ask ("pointing at the graduation rule").</param>
/// <param name="Reason">Human-readable detail: why this namespace is reserved (e.g. which gh-# issue
/// names the subsystem as born-outside, or which project it graduated to).</param>
internal sealed record HostNamespaceReservation(string ReservedNamespace, string RulingReference, string Reason);
