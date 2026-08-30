using System.Diagnostics.CodeAnalysis;

namespace GenWave.Core.Domain;

/// <summary>
/// The result of asking an <see cref="Abstractions.IFileActionPlanner"/> to plan an action (SPEC
/// F154.3; STORY-379; PLAN T379, gh-#529) — either a <see cref="Plan"/> or a <see cref="Refusal"/>,
/// never both, never neither. A small discriminated shape (two nullable properties plus
/// <see cref="IsRefused"/>, built only through the static factories below) rather than a closed type
/// hierarchy: the planner returns one of these on every call, so it stays allocation-light and the
/// caller's own branch (<c>if (result.IsRefused)</c>) reads exactly like the SPEC's own two-phase
/// "plan or refuse" language.
/// </summary>
public sealed class FileActionPlanResult
{
    /// <summary>The prepared plan, or <see langword="null"/> when <see cref="IsRefused"/>.</summary>
    public FileActionPlan? Plan { get; }

    /// <summary>The refusal, or <see langword="null"/> when a plan was produced.</summary>
    public FileActionRefusal? Refusal { get; }

    /// <summary>True when the planner refused — equivalent to <c><see cref="Refusal"/> is not null</c>.
    /// The two <see cref="MemberNotNullWhenAttribute"/> annotations let a caller's own
    /// <c>if (!result.IsRefused)</c>/<c>if (result.IsRefused)</c> branch narrow
    /// <see cref="Plan"/>/<see cref="Refusal"/> to non-null without a null-forgiving operator.</summary>
    [MemberNotNullWhen(false, nameof(Plan))]
    [MemberNotNullWhen(true, nameof(Refusal))]
    public bool IsRefused => Refusal is not null;

    FileActionPlanResult(FileActionPlan? plan, FileActionRefusal? refusal)
    {
        Plan = plan;
        Refusal = refusal;
    }

    /// <summary>A successful plan, ready for a token to be minted from it.</summary>
    public static FileActionPlanResult Planned(FileActionPlan plan) => new(plan, null);

    /// <summary>A refusal naming <paramref name="rule"/> — carries no path, no target, no operator
    /// input (F154.3).</summary>
    public static FileActionPlanResult Refused(FileActionRule rule) => new(null, new FileActionRefusal(rule));
}
