using GenWave.Abstractions.Playout;

namespace GenWave.Host.Api;

/// <summary>
/// Discriminated union expressing the outcome of parsing <c>PUT /api/shows/{id}</c>'s body (SPEC
/// F152.5, STORY-373, PLAN T362) — mirrors <see cref="Core.Domain.ShowWriteResult"/>'s own
/// closed-hierarchy/private-constructor shape. <see cref="ShowRotationController"/>'s own
/// <c>ParseRotationBody</c> is the one producer; its own <c>SetRotation</c> is the one consumer.
/// </summary>
public abstract record ShowRotationBodyResult
{
    private ShowRotationBodyResult() { }

    /// <summary>
    /// The body carried no <c>"rotation"</c> property at all — SPEC F152.5's "absent = leave
    /// unchanged": <c>SetRotation</c> never calls <see cref="Core.Abstractions.IShowStore.SetRotationAsync"/>
    /// for this case, it simply re-reads and echoes the show as it already stands.
    /// </summary>
    public sealed record Unchanged : ShowRotationBodyResult;

    /// <summary>
    /// The body carried <c>"rotation": null</c> explicitly — SPEC F152.5's "explicit null = remove
    /// the rule": <c>SetRotation</c> calls <see cref="Core.Abstractions.IShowStore.SetRotationAsync"/>
    /// with a <see langword="null"/> predicate.
    /// </summary>
    public sealed record Cleared : ShowRotationBodyResult;

    /// <summary>A well-shaped, in-bound rotation object — ready to persist verbatim.</summary>
    public sealed record Valid(RotationPredicate Rotation) : ShowRotationBodyResult;

    /// <summary>
    /// The body's <c>"rotation"</c> property is malformed or out of bound (SPEC F152.5's own three
    /// validation rules — at least one bound set, <c>maxPlays</c> ≥ 0, <c>notAiredWithinDays</c>
    /// 1–3650 — plus basic shape checks). <see cref="Field"/> names the offending wire field (e.g.
    /// <c>"maxPlays"</c>, <c>"notAiredWithinDays"</c>, or <c>"rotation"</c> itself for the
    /// no-bound-set case) for the 400 body.
    /// </summary>
    public sealed record Invalid(string Field, string Detail) : ShowRotationBodyResult;
}
