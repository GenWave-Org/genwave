namespace GenWave.Core.Domain;

/// <summary>
/// What <see cref="Abstractions.IFileActionExecutor.ExecuteAsync"/> returns for one attempt (SPEC
/// F154.4, F154.6-F154.8; STORY-379; PLAN T380, gh-#529). <see cref="Rule"/> is populated only for
/// <see cref="FileActionOutcomeKind.Refused"/> — the SAME closed <see cref="FileActionRule"/> set
/// <see cref="Abstractions.IFileActionPlanner"/> itself refuses with; F154.3's own "path never
/// echoed" posture continues here — this type carries a rule, never a path.
/// </summary>
/// <param name="Kind">Which of the six outcomes this attempt landed on.</param>
/// <param name="Rule">The refusal reason, for <see cref="FileActionOutcomeKind.Refused"/> only;
/// <see langword="null"/> for every other <see cref="Kind"/>.</param>
public sealed record FileActionOutcome(FileActionOutcomeKind Kind, FileActionRule? Rule);
