namespace GenWave.Core.Domain;

/// <summary>
/// The feeder's "pushed but never aired" diagnostic (gh-#612), published on
/// <c>PlayoutFeeder.PushLoss</c> once the safe rotation has covered
/// <c>PlayoutFeeder.SafeCoverTicksBeforePushLossSignal</c> consecutive observe ticks while pushes
/// this feeder made are still unproven (claim (d)'s pending-air queue is non-empty). That pairing —
/// "the engine says nothing of ours is airing" AND "we believe we fed it" — is the signature of a
/// push that died engine-side after a success-shaped reply (RID allocated, request killed at
/// resolution), the failure shape that ran silently for seven days in the gh-#610 incident.
/// <para>
/// Deliberately a value record: the Host shell logs a WARN only when the signal CHANGES. During a
/// continuing outage each confirmed-drain refill strips the abandoned chain's claims and pushes a
/// fresh one, so the oldest-pending id is a NEW id every replan cycle — the signal changes once per
/// cycle (~a safe-track airing), which re-warns loud enough to page and bounded enough to never
/// flood a tick-per-3s loop.
/// </para>
/// </summary>
/// <param name="OldestPendingId">The oldest pushed-and-unproven id — the current retry chain's first
/// casualty (each drain-confirmed replan strips the previous chain's claims, so a continuing episode
/// presents a fresh id per cycle).</param>
/// <param name="Title">The oldest pending push's title, when its metadata is still held.</param>
/// <param name="Artist">The oldest pending push's artist, when its metadata is still held.</param>
/// <param name="PendingCount">How many pushes are currently unproven.</param>
public sealed record PushLossSignal(string OldestPendingId, string? Title, string? Artist, int PendingCount);
