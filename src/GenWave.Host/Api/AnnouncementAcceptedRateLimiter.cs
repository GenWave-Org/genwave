using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// The station-wide ACCEPTED-rate cap for <c>POST /api/announcements</c> (SPEC F143.4, STORY-357,
/// PLAN T339 review finding F1) — a single <see cref="FixedWindowRateLimiter"/> shared by every
/// caller, acquired by <see cref="AnnouncementsController.Post"/> ONLY after every other refusal gate
/// (SpectatorMode, message/voice length, ttl bounds, pending depth) has already let the request
/// through, so a refused request never spends a permit from this budget.
///
/// <para>
/// <b>Replaces the former rate-limiter MIDDLEWARE policy (F1's finding).</b>
/// <see cref="RateLimiterPolicies"/> used to carry an <c>Announce</c> policy applied via
/// <c>[EnableRateLimiting]</c>, which — because <c>UseRateLimiter()</c> runs BEFORE
/// <c>UseAuthentication()</c> in the pipeline (deliberate, Program.cs's own ordering) — counted every
/// anonymous 401, every 403/400/depth-429 refusal, and every accepted 200 against the SAME budget: an
/// unauthenticated prober could drain the operator's only window (a LAN denial-of-service on the House
/// Voice), and the framework's own 429 has an empty body (no <c>ProblemDetails</c>, violating this
/// feature's "every decline is visible with an honest reason" law). Deleted, not repurposed per-IP:
/// SPEC F143.4's cap is a station-wide "misfire-flood protection for the break system" budget, not a
/// per-caller one (see <see cref="RateLimiterPolicies"/>'s own remaining remarks for that partitioning
/// rationale, which still holds — only WHERE it is enforced changed). This singleton is the honest
/// replacement: it is reachable only for a caller already past every other gate, so it counts exactly
/// what SPEC F143.4 names — ACCEPTED submissions — and its own 429
/// (<see cref="AnnouncementsController.Post"/>'s own <c>ProblemDetails</c>) always carries a body.
/// </para>
///
/// <para>
/// <b>Options are read ONCE, at construction.</b> <see cref="FixedWindowRateLimiterOptions.PermitLimit"/>
/// is fixed the moment the underlying <see cref="FixedWindowRateLimiter"/> is built — the framework has
/// no live-reconfigure path for an already-constructed limiter, so this takes a plain
/// <see cref="IOptions{TOptions}"/> rather than <see cref="IOptionsMonitor{TOptions}"/>, matching
/// <c>AnnouncementsOptions</c>' own env-only, not-live-monitored posture (that class's own remarks): a
/// live edit to <c>Announcements:AcceptedPerMinute</c> needs a process restart to take effect, exactly
/// like <c>MessageMaxChars</c>/<c>PendingDepthCap</c> already do everywhere else in this feature.
/// </para>
///
/// <b>Lifetime.</b> Singleton — one shared window for the whole process, the same "one instance IS the
/// shared state" shape every fixed-window limiter in <see cref="RateLimiterPolicies"/> already uses,
/// just resolved through DI instead of a middleware partition closure.
/// </summary>
// public — AnnouncementsController (public) takes this as a constructor parameter; a public
// controller cannot expose a less-accessible type in its own constructor signature (CS0051).
public sealed class AnnouncementAcceptedRateLimiter : IDisposable
{
    readonly FixedWindowRateLimiter limiter;

    public AnnouncementAcceptedRateLimiter(IOptions<AnnouncementsOptions> options)
    {
        limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.Value.AcceptedPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    /// <summary>
    /// Attempts to consume one permit from the current window. Never blocks or queues
    /// (<see cref="FixedWindowRateLimiterOptions.QueueLimit"/> is 0) — the caller gets an immediate
    /// yes/no, the same "refuse, never wait" posture every other policy in
    /// <see cref="RateLimiterPolicies"/> shares.
    /// </summary>
    public bool TryAcquire()
    {
        // Dispose the lease even though FixedWindowRateLimiter never returns permits on dispose today
        // — a future ConcurrencyLimiter swap would leak a permit per call without this.
        using var lease = limiter.AttemptAcquire();
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
