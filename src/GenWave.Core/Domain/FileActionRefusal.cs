namespace GenWave.Core.Domain;

/// <summary>
/// Why an <see cref="Abstractions.IFileActionPlanner"/> refused to plan an action (SPEC F154.3;
/// STORY-379; PLAN T379, gh-#529). Carries <see cref="Rule"/> and NOTHING else — deliberately no
/// path, no target, no operator input of any kind: F154.3 requires the rule to be named without ever
/// echoing what was refused. A reflection-pinned test (Story379) proves this type exposes no
/// <see cref="string"/>-typed member at all, so a future edit can't quietly reintroduce one.
/// </summary>
/// <param name="Rule">The one rule that refused the plan.</param>
public readonly record struct FileActionRefusal(FileActionRule Rule);
