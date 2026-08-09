namespace GenWave.Context;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// One provider's content, ready for the segment lane (SPEC F107.2/F107.3): <see cref="ContextPipeline.TickAsync"/>
/// returns these to the T226 Host ticker, which enqueues one <c>SegmentKind.ContextSegment</c>
/// deferral per entry (T223). Handed off at most once per cadence slot per provider — the ticker
/// polling more often than the cadence never sees the same slot's content twice.
/// </summary>
/// <param name="Key">The provider's <see cref="IContextProvider.Key"/> — the deferral queue's
/// per-<c>(kind, discriminator)</c> supersede key (F107.4).</param>
/// <param name="Content">The fresh, non-blank-<see cref="ContextContent.SegmentFacts"/> content due
/// this slot.</param>
public sealed record DueContextSegment(string Key, ContextContent Content);
