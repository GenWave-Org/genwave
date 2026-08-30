using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// One <c>library.file_action</c> row, fully formed and ready to insert (SPEC F154.7; STORY-379;
/// PLAN T380, gh-#529) — <c>FileActionExecutor</c> builds one for every attempt, successful or not;
/// <c>FileActionRepository.AuditAsync</c>/<c>RelocateAsync</c> are its only two writers.
/// </summary>
/// <param name="MediaId">The row this attempt was about.</param>
/// <param name="Verb">Which of the three actions was attempted.</param>
/// <param name="FromPath">The subject's path as of the plan — <c>FileActionPlan.From</c> verbatim.</param>
/// <param name="ToPath">The computed destination, or <see langword="null"/> for
/// <see cref="FileActionVerb.Retag"/> (its destination is its source — SPEC F154.7's own "to_path
/// NULL for retag" shape).</param>
/// <param name="PlanToken">The confirm request's own plan token, carried through unread.</param>
/// <param name="Outcome">The wire outcome token (<c>done</c>, <c>conflict</c>, <c>refused</c>,
/// <c>failed</c>, <c>reverted</c>, or <c>busy</c>).</param>
/// <param name="DetailJson">The pre-serialized <c>jsonb</c> body — <c>{}</c> for every outcome except
/// <c>refused</c> (<c>{"rule": "..."}</c>) and <c>failed</c>/<c>reverted</c>
/// (<c>{"reason": "io"|"db"[, "revert": false]}</c>).</param>
sealed record FileActionAuditEntry(
    long MediaId,
    FileActionVerb Verb,
    string FromPath,
    string? ToPath,
    string PlanToken,
    string Outcome,
    string DetailJson);
