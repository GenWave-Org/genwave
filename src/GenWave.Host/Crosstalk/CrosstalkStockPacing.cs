namespace GenWave.Host.Crosstalk;

/// <summary>
/// SPEC F140 (STORY-354, PLAN T328) — the pacing state <see cref="CrosstalkStockWorker"/> consults
/// before starting a generation attempt and updates once every attempt resolves. A plain mutable
/// accumulator (the ONE dependency is the <see cref="ILogger"/> its own two required log lines need,
/// SPEC F140.4) — unit-testable with no worker, no timer, no network, mirroring this folder's own
/// "logic stays in a framework-free collaborator" precedent one level down from
/// <c>CrosstalkBreakWindow</c>.
///
/// <para>
/// <b>The rolling estimate (F140.2).</b> Seeded at <see cref="SeedEstimate"/> (gh-#546's own observed
/// 8–18s generation range, rounded up) and updated by a simple 50/50 exponentially-weighted blend
/// (<c>estimate = (estimate + observed) / 2</c> — "a simple exponentially-weighted or windowed mean is
/// fine, no over-engineering" per PLAN T328). A COMPLETED generation (<see cref="RecordCompleted"/> —
/// assembled OR a genuine script/render discard; either way the attempt ran to its own natural end,
/// never cut off) blends unconditionally: its elapsed time is a genuine sample of how long a full
/// attempt actually takes. An ABANDONED generation (<see cref="RecordAbandoned"/> — the watchdog
/// cancelled it mid-flight because a break window opened, SPEC F140.2's "every cancellation") only
/// blends when the observed in-flight time EXCEEDS the current estimate: being cut off after 3s proves
/// nothing about how long the attempt would have taken to finish, but being cut off after (say) 25s
/// against a 20s estimate is real evidence the estimate was already too low. Blending only upward here
/// stops a string of early cancellations from silently eroding the estimate toward zero — the opposite
/// of what a gap-aware gate needs when abandons are actually happening.
/// </para>
///
/// <para>
/// <b>Backoff (F140.3).</b> Each consecutive abandon (never a discard — see
/// <see cref="RecordAbandoned"/>'s own remarks) doubles the delay from the caller-supplied base cadence
/// (<c>baseCadence * 2^consecutiveAbandons</c>), capped at <see cref="MaxBackoff"/>; a completed
/// generation resets the streak to zero and clears the delay entirely (<see cref="RecordCompleted"/>).
/// Exactly ONE Information line logs the ENGAGE transition (streak 0→1) and ONE logs the RELEASE
/// transition (an engaged streak dropping back to 0) — never one per abandon within an
/// already-engaged streak, and never one per tick a caller declines to attempt because
/// <see cref="IsBackedOff"/> reads true (SPEC F140.4/the gh-#558 lesson: "log nothing per skipped
/// tick").
/// </para>
/// </summary>
internal sealed class CrosstalkStockPacing(ILogger log, TimeSpan baseCadence)
{
    /// <summary>SPEC F140.2's own seed — gh-#546's observed 8–18s generation range, rounded up.</summary>
    public static readonly TimeSpan SeedEstimate = TimeSpan.FromSeconds(20);

    /// <summary>SPEC F140.3's own cap — "any completed generation resets" is the only way back to the
    /// base cadence; this is the ceiling a runaway streak of abandons can never cross.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    // Math.Pow(2, consecutiveAbandons) overflows a double (and then TimeSpan's own multiply throws)
    // long before a realistic streak ever gets here — MaxBackoff caps the RESULT at 5 minutes on
    // attempt 4 already (base 20s: 40/80/160/320→300), so this only guards a pathological streak count
    // from ever reaching Math.Pow's own blow-up range.
    const int MaxDoublingExponent = 20;

    int consecutiveAbandons;
    DateTimeOffset? backoffUntil;

    /// <summary>The current rolling generation-time estimate — SPEC F140.1's runway gate compares
    /// clear runway against THIS value.</summary>
    public TimeSpan Estimate { get; private set; } = SeedEstimate;

    /// <summary>SPEC F140.4's "counted, not logged" tally — a worker-lifetime running total, never
    /// reset. Round-2 review finding F2: this property itself is internal (test-only — a live daemon
    /// has no way to read it); the operator-facing surface for the SAME number is the "after
    /// {RunwaySkips} runway skips" fragment <see cref="Engage"/> folds into its own ENGAGE Information
    /// line below, not a per-skip line (SPEC F140.4, the gh-#558 lesson: "log nothing per skipped
    /// tick").</summary>
    public int RunwaySkips { get; private set; }

    /// <summary>The instant the current backoff delay expires, or <see langword="null"/> when no
    /// streak is in effect — read directly (rather than only through <see cref="IsBackedOff"/>'s bool)
    /// so a test can pin the doubling/cap math itself without waiting on wall-clock time.</summary>
    public DateTimeOffset? BackoffUntil => backoffUntil;

    /// <summary>Is a backed-off delay still in effect right now? <see cref="CrosstalkStockWorker.TickOnceAsync"/>
    /// consults this before doing anything else — see this type's own remarks for why a tick spent
    /// inside this window logs and counts nothing.</summary>
    public bool IsBackedOff(DateTimeOffset now) => backoffUntil is { } until && now < until;

    /// <summary>SPEC F140.1: a tick that found insufficient runway to even attempt — counted, per this
    /// type's own remarks, never logged.</summary>
    public void RecordRunwaySkip() => RunwaySkips++;

    /// <summary>A generation ran to its own natural end — assembled or genuinely discarded, either way
    /// NOT cut off by the break-window watchdog (see <see cref="RecordAbandoned"/> for that half).
    /// Blends <paramref name="elapsed"/> into <see cref="Estimate"/> unconditionally and, if a backoff
    /// streak was in effect, releases it (SPEC F140.3, one Information line).</summary>
    public void RecordCompleted(TimeSpan elapsed)
    {
        Estimate = Blend(Estimate, elapsed);
        Release();
    }

    /// <summary>The watchdog cancelled an in-flight generation because a break window opened (SPEC
    /// F140.2's "every cancellation"). Blends <paramref name="elapsed"/> into <see cref="Estimate"/>
    /// only when it exceeds the current value (this type's own remarks — a lower bound, not a full
    /// sample) and engages/extends the backoff streak (SPEC F140.3).</summary>
    public void RecordAbandoned(TimeSpan elapsed, DateTimeOffset now)
    {
        if (elapsed > Estimate)
            Estimate = Blend(Estimate, elapsed);

        Engage(now);
    }

    static TimeSpan Blend(TimeSpan current, TimeSpan observed) =>
        current + TimeSpan.FromTicks((observed - current).Ticks / 2);

    void Engage(DateTimeOffset now)
    {
        consecutiveAbandons++;

        var exponent = Math.Min(consecutiveAbandons, MaxDoublingExponent);
        var delay = baseCadence * Math.Pow(2, exponent);
        if (delay > MaxBackoff)
            delay = MaxBackoff;

        var wasEngaged = backoffUntil is not null;
        backoffUntil = now + delay;

        if (!wasEngaged)
        {
            log.LogInformation(
                "Crosstalk stock backoff engaged after a break-window abandon — next attempt no sooner " +
                "than {DelaySeconds}s (after {RunwaySkips} runway skips)",
                delay.TotalSeconds, RunwaySkips);
        }
    }

    void Release()
    {
        if (backoffUntil is null)
            return;

        consecutiveAbandons = 0;
        backoffUntil = null;
        log.LogInformation("Crosstalk stock backoff released — a generation completed");
    }
}
