namespace GenWave.Core.Domain;

/// <summary>
/// At most one due show-flavor line for the current break's patter prompt (SPEC F116.3, STORY-308,
/// PLAN T249) — <see cref="Abstractions.IShowFlavorLineSource.TryTakeDueShowLine"/>'s return shape, and
/// <c>GenWave.Orchestration.ShowFlavorLineGate.TryTakeDueShowLine</c>'s own return shape (that class
/// implements the interface above; Orchestration references Core, never the reverse — see that
/// interface's own remarks for why this record lives HERE, in Core, mirroring
/// <see cref="ContextPatterFact"/>'s own placement one seam over). Deliberately minimal and internal to
/// the repo, not part of the published MIT <c>GenWave.Abstractions</c> surface (F105.6): nothing
/// outside this codebase's own patter lane (<c>GenWave.Tts.LlmCopyWriter</c>, PLAN T249) consumes it.
/// </summary>
/// <param name="ShowName">The on-air show's display name, verbatim off
/// <c>Abstractions.Playout.OnAirSnapshot.Show</c> — never re-derived.</param>
/// <param name="Flavor">The show's flavor text, verbatim off that same snapshot. The gate never hands
/// out a fact with blank flavor (nothing to say), so this is always non-blank when this record
/// exists.</param>
public sealed record ShowFlavorFact(string ShowName, string Flavor);
