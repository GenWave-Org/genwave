namespace GenWave.Core.Domain;

/// <summary>
/// Outcome of <see cref="Abstractions.IAdminMediaWrite.UpdateReturningVersionAsync"/> (STORY-103,
/// Epic R). Pairs the existing <see cref="MediaWriteResult"/> outcome with the row's new <c>xmin</c>
/// token, read straight from the UPDATE's <c>RETURNING</c> clause — never a follow-up SELECT.
///
/// <see cref="NewVersion"/> is populated only when <see cref="Result"/> is
/// <see cref="MediaWriteResult.Updated"/>; every other outcome carries <c>null</c> so a caller can
/// never mistake a failed write for a fresh version.
///
/// <see cref="LibraryId"/> is the row's post-write library id, from the same <c>RETURNING</c> clause
/// (or no-op-branch SELECT) as <see cref="NewVersion"/> — added for SPEC F43.2 (Epic V) so a caller
/// can add the <c>X-Out-Of-Scope</c> warning header without a second read when the write did not
/// request a library reassignment. Also populated only when <see cref="Result"/> is
/// <see cref="MediaWriteResult.Updated"/>.
/// </summary>
public readonly record struct MediaUpdateOutcome(MediaWriteResult Result, string? NewVersion, long? LibraryId = null);
