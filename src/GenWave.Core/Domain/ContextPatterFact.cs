namespace GenWave.Core.Domain;

/// <summary>
/// At most one compact fact for the current break's patter prompt (SPEC F107.5, STORY-298) —
/// <see cref="Abstractions.IContextPatterFactSource.TryTakeDuePatterFact"/>'s return shape, and
/// <c>GenWave.Context.ContextPipeline.TryTakeDuePatterFact</c>'s own return shape (that class
/// implements the interface above; Context references Core, never the reverse — see that
/// interface's own remarks for why this record lives HERE, in Core, rather than in Context).
/// Deliberately minimal and internal to the repo, not part of the published MIT
/// <c>GenWave.Abstractions</c> surface (F105.6): nothing outside this codebase's own patter lane
/// (<c>GenWave.Tts.LlmCopyWriter</c>, PLAN T225) consumes it.
/// </summary>
/// <param name="Key">The originating provider's <c>IContextProvider.Key</c>
/// (<c>GenWave.Abstractions</c>), for diagnostics only — the patter prompt itself carries only
/// <see cref="Fact"/>.</param>
/// <param name="Fact">The compact, ready-to-read fact text — <see cref="ContextContent.PatterFact"/>
/// verbatim, never re-derived.</param>
public sealed record ContextPatterFact(string Key, string Fact);
