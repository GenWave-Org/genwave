namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/schedule/assign-show</c>'s success body (SPEC F119.2, STORY-313, PLAN T243):
/// <see cref="UpdatedBlockIds"/> names every <c>segment_schedule</c> row the call actually touched —
/// the requested block alone when narrowed, or every block of its contiguous same-persona run
/// otherwise — so the T245 grid client can re-render exactly those cells without a follow-up
/// <c>GET /api/schedule</c>.
///
/// <para>
/// <see cref="Version"/> is <see cref="GenWave.Core.Domain.ShowAssignResult.Assigned.Version"/> passed
/// straight through —
/// the week's fresh <c>GenWave.Core.Domain.ScheduleWeekVersion</c> content fingerprint, computed from
/// the same post-write rows this call touched. Wire-shaped identically to
/// <c>ScheduleWeekDto.Version</c> (gh-#255's own optimistic-concurrency token) so a client can treat
/// this response exactly like a fresh GET for the purpose of a subsequent
/// <c>PUT /api/schedule</c>'s <c>BaseVersion</c> — an assign-then-repaint flow never has to issue a
/// throwaway GET just to learn the version its own write already produced.
/// </para>
/// </summary>
public sealed record AssignShowResponseDto(IReadOnlyList<long> UpdatedBlockIds, string Version);
