namespace GenWave.Host.Tests.Support;

/// <summary>
/// One GenWave-owned interface port the composition root registers, plus every registration ever
/// made against it (see <see cref="SeamAdapterEntry"/> for why those are labeled "also registered,"
/// never "shadowed"/"overridden") — T216's raw unit of "what SEAMS.md renders one row from."
/// Deliberately holds a <see cref="Type"/>, not a pre-formatted name string: naming/formatting is a
/// rendering concern that belongs to whoever turns this into markdown (<c>SeamIndexDocument</c>, in
/// <c>tools/SeamIndexGenerator</c>), not to the DI-mechanics side that builds it. That same project's
/// <c>DecoratorChain</c> derives layered/wrapped adapters (e.g. `ISegmentCopyWriter`'s
/// `DegradationGatedCopyWriter` wrapping `LlmCopyWriter`/`TemplateCopyWriter`) by reflecting on the
/// effective adapter's own constructor — a separate concern from this type, which only ever records
/// what the container itself registered, never what an adapter's constructor composes internally.
/// </summary>
public sealed record SeamPort(Type PortType, IReadOnlyList<SeamAdapterEntry> Adapters);
