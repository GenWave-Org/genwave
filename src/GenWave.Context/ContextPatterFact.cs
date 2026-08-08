namespace GenWave.Context;

/// <summary>
/// At most one compact fact for the current break's patter prompt (SPEC F107.5, STORY-298) —
/// <see cref="ContextPipeline.TryTakeDuePatterFact"/>'s return shape. Deliberately minimal and
/// internal to the repo, not part of the published MIT <c>GenWave.Abstractions</c> surface: nothing
/// outside this codebase's own patter lane (T225) consumes it.
/// </summary>
/// <param name="Key">The originating provider's <see cref="GenWave.Core.Abstractions.IContextProvider.Key"/>,
/// for diagnostics only — the patter prompt itself carries only <see cref="Fact"/>.</param>
/// <param name="Fact">The compact, ready-to-read fact text — <see cref="GenWave.Core.Domain.ContextContent.PatterFact"/>
/// verbatim, never re-derived.</param>
public sealed record ContextPatterFact(string Key, string Fact);
