namespace GenWave.Orchestration;

/// <summary>
/// A dropped <see cref="SegmentKind.ContextSegment"/> slot's WARN names the provider it dropped from
/// (SPEC F107.6) — the same <paramref name="ProviderKey"/> <c>PlannedSlot.ContextProviderKey</c> used
/// to carry, now held on the policy instead so the render phase's drop report never has to walk back
/// to the deferral that produced it.
/// </summary>
/// <param name="ProviderKey">The context provider's own key, or <see langword="null"/> when unknown.</param>
public sealed record ContextProviderWarnSubject(string? ProviderKey) : WarnDropSubject;
