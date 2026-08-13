namespace GenWave.Context;

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// SPEC F107.2/F107.6 — owns every registered <see cref="IContextProvider"/>'s cadence-slot state
/// and freshness cache. Single responsibility: decide WHEN a provider may fetch and WHETHER its
/// cached content is currently servable; it never renders copy, never picks a voice, and never talks
/// to a queue — those are the T223/T224/T225 consumers' jobs, reached through <see cref="TickAsync"/>
/// (segment lane) and <see cref="TryTakeDuePatterFact"/> (patter lane, fulfilling
/// <see cref="IContextPatterFactSource"/> — PLAN T225's seam for <c>GenWave.Tts.LlmCopyWriter</c> to
/// depend on without a project reference to this L1 project).
///
/// <para>
/// <b>Fetch-once-per-slot.</b> Time is bucketed into fixed-width windows of
/// <see cref="ContextProviderSettings.SegmentCadenceMinutes"/> minutes — floored at 1, and further
/// floored per-provider by <see cref="ICadenceFlooredContextProvider.MinimumSegmentCadenceMinutes"/>
/// when a provider opts in (F4 fix, T226 review, SPEC F108.2) — and floor-divided from the
/// <see cref="TimeProvider"/> epoch, so every instance buckets identically without needing to agree
/// on a start time. A provider's <see cref="IContextProvider.FetchAsync"/> is invoked AT MOST ONCE
/// per slot, on whichever tick first lands in it — success, null, or a thrown exception all count as
/// "attempted this slot" and none of them trigger a retry before the next slot begins.
/// </para>
///
/// <para>
/// <b>Freshness.</b> A slot's fetch result is cached in-process; <see cref="ContextContent.FreshUntil"/>
/// is re-checked against the current time on every read (not just at fetch time), so content that
/// outlives its own freshness mid-slot — cadence wider than the content's own shelf life — stops
/// being served immediately, with no wait for the next slot's fetch.
/// </para>
///
/// <para>
/// <b>Skip-never-silence (F107.6).</b> A stale/failed/empty fetch produces no segment and no patter
/// fact, with exactly one <see cref="LogLevel.Information"/> line per cadence slot naming the
/// provider's <see cref="IContextProvider.Key"/> and the cause — never a warning or error (an
/// external context source being unavailable is ordinary operation, not a fault), and never the
/// provider's own facts (F108.3 forbids echoing coordinates or any other provider-authored content
/// into a log line). Two skip-never-silence causes are NOT governed by that per-slot cadence (F7 fix,
/// T222 review; F2 fix, T227 review) — both are long-lived operator-controlled STATES, not transient
/// fetch outcomes, so logging them every slot forever would mean one Information line per registered
/// provider every cadence tick, out of the box, for as long as an operator leaves them that way:
/// <list type="bullet">
/// <item>DISABLED: <see cref="ContextProviderSettings.SegmentCadenceMinutes"/> clamps to one minute
/// for a disabled provider (whose configured cadence is typically still the zero-value default), so a
/// per-slot log there would mean one line EVERY MINUTE. Logs on the enabled→disabled transition edge
/// only (the gh-#338 precedent).</item>
/// <item>SELF-GATED UNAVAILABLE (<see cref="ISelfGatingContextProvider"/>): a provider that can tell
/// this pipeline it has nothing to produce WITHOUT a fetch (e.g. <c>WeatherContextProvider</c>'s
/// F108.1 fail-closed coordinate check) gets the exact same edge-triggered treatment, via the same
/// mechanism, on its own independent edge — checked in BOTH <see cref="TickAsync"/> and
/// <see cref="TryTakeDuePatterFact"/> (a review-round fix: checking it in only one lane left the
/// other free to keep vending a provider's last-fetched content for up to its own
/// <see cref="ContextContent.FreshUntil"/> after the provider went unavailable). The SAME edge also
/// clears that provider's cached content (<c>ProviderState.ClearContent</c>) — nothing repopulates
/// it while unavailable, so this is the one edit that keeps every reader of
/// <c>ProviderState.Content</c> honest, present callers and future ones alike, not just the two
/// lanes that happen to gate on <see cref="ISelfGatingContextProvider.IsAvailable"/> today.</item>
/// </list>
/// Both: one line when first observed in that state, silence for as long as it stays that way, and a
/// fresh line the next time it re-enters that state after leaving it.
/// </para>
///
/// <para>
/// <b>Thread safety.</b> <see cref="TickAsync"/> (the T226 Host ticker, one fixed-interval caller) and
/// <see cref="TryTakeDuePatterFact"/> (the T225 patter/copywriter lane) read and read-modify-write the
/// SAME per-provider <see cref="ProviderState"/> instances from what are, in production, two different
/// threads. Every field on <see cref="ProviderState"/> is therefore guarded by that instance's own
/// internal lock — this class never locks externally and never reaches into a
/// <see cref="ProviderState"/>'s fields directly, only through its lock-guarded methods/properties.
/// The <see cref="states"/> dictionary itself needs no lock: its key set is fixed for the lifetime of
/// this instance (populated once, in the constructor, before any caller can observe this instance at
/// all), so concurrent reads of it are always safe.
/// </para>
/// </summary>
public sealed partial class ContextPipeline : IContextPatterFactSource
{
    [GeneratedRegex("^[a-z0-9-]+\\z")]
    private static partial Regex KeyPattern();

    /// <summary>SPEC F125.3's segment window size — at most this many facts joined per segment vend.
    /// A provider whose airable <see cref="ContextContent.Facts"/> is no longer than this (History's
    /// typical 2-4 curated entries) sees a window that always covers the whole list, in order, every
    /// vend — the pre-F125 shape, now expressed as this algorithm's degenerate case rather than a
    /// separate code path. Was <c>HistoryContextProvider.MaxSegmentEntries</c> before F125.2 moved
    /// segment selection from the provider to this pipeline.</summary>
    const int SegmentWindowFacts = 4;

    /// <summary>Joins a segment window's facts into one string — never a newline (SPEC F109.2's own
    /// explicit requirement; also enforced structurally by <see cref="ContextFactSanitizer"/> upstream
    /// of this join, belt-and-suspenders). Was <c>HistoryContextProvider.FactSeparator</c> before
    /// F125.2 moved the join here so it could rotate.</summary>
    const string FactSeparator = " · ";

    readonly IReadOnlyList<IContextProvider> providers;
    readonly IContextSettingsProvider settingsProvider;
    readonly TimeProvider timeProvider;
    readonly ILogger<ContextPipeline> logger;
    readonly Dictionary<string, ProviderState> states;

    /// <summary>
    /// Fails fast (SPEC F107.1's Key contract, T221 review carry-forward) on a duplicate or
    /// invalid-format provider key — a misconfigured provider set is a construction-time bug, never a
    /// runtime one discovered mid-tick.
    /// </summary>
    /// <exception cref="ArgumentException">A provider's <see cref="IContextProvider.Key"/> is not
    /// lowercase-ASCII/digits/hyphen, or collides with another registered provider's key.</exception>
    public ContextPipeline(
        IEnumerable<IContextProvider> providers,
        IContextSettingsProvider settingsProvider,
        TimeProvider timeProvider,
        ILogger<ContextPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(settingsProvider);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        var providerList = providers.ToList();
        states = new Dictionary<string, ProviderState>(providerList.Count, StringComparer.Ordinal);
        foreach (var provider in providerList)
        {
            if (!KeyPattern().IsMatch(provider.Key))
                throw new ArgumentException(
                    $"Context provider key \"{provider.Key}\" is invalid — keys must be lowercase " +
                    "ASCII letters, digits, and hyphens only.",
                    nameof(providers));

            if (!states.TryAdd(provider.Key, new ProviderState()))
                throw new ArgumentException(
                    $"Context provider key \"{provider.Key}\" is registered more than once — " +
                    "keys must be unique.",
                    nameof(providers));
        }

        this.providers = providerList;
        this.settingsProvider = settingsProvider;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>
    /// Advances every registered provider by one tick: fetches whichever providers just entered a
    /// new cadence slot (at most once each, per the class remarks), then returns every provider whose
    /// cached content is fresh, carries at least one airable <see cref="ContextContent.Facts"/> entry,
    /// and has not already been handed off for its current slot. A handed-off provider's segment text
    /// is selected HERE, at vend time (SPEC F125.2/F125.3): a rotating window through its airable
    /// list, joined and returned as the due <see cref="DueContextSegment"/>. Safe to call more often
    /// than any provider's cadence — the T226 Host ticker is the one, dumb, fixed-interval caller.
    /// </summary>
    public async Task<IReadOnlyList<DueContextSegment>> TickAsync(CancellationToken ct)
    {
        var due = new List<DueContextSegment>();
        var now = timeProvider.GetUtcNow();

        foreach (var provider in providers)
        {
            ct.ThrowIfCancellationRequested();
            var settings = settingsProvider.For(provider.Key);
            var state = states[provider.Key];

            if (!settings.Enabled)
            {
                // Edge-triggered (F7): logs once on the enabled→disabled transition, silent for every
                // tick after that until this provider is next observed enabled — see the class remarks.
                if (state.NoteEnabled(false))
                {
                    logger.LogInformation(
                        "Context provider {ProviderKey} produced no output: disabled.", provider.Key);
                }

                continue;
            }

            state.NoteEnabled(true); // Re-arms the disabled-edge log for the NEXT time this provider goes disabled.

            if (provider is ISelfGatingContextProvider gated && !gated.IsAvailable)
            {
                // Edge-triggered (F2 fix, T227 review) — the exact mirror of the disabled branch
                // above, on its own independent edge (see the class remarks). Crucially, this never
                // calls FetchAsync: that is what keeps EnsureFetchedAsync's own "fetch returned no
                // content" line — an honest cause for a GENUINE null reply — from also firing for a
                // cause that was never a fetch attempt at all.
                if (state.NoteAvailable(false))
                {
                    // Invalidates whatever this provider fetched before going unavailable (F2/F3
                    // interaction fix, T227 re-review): without this, content already cached from a
                    // healthy slot would keep reading back from ProviderState.Content — and, before
                    // TryTakeDuePatterFact's own gate below existed, kept being VENDED — for up to
                    // its own FreshUntil, long after the operator broke the config. Nothing
                    // repopulates content while unavailable (EnsureFetchedAsync is never reached
                    // below this branch), so clearing once, here, on the edge, covers the entire
                    // unavailable streak.
                    state.ClearContent();
                    logger.LogInformation(
                        "Context provider {ProviderKey} produced no output: misconfigured.", provider.Key);
                }

                continue;
            }

            state.NoteAvailable(true); // Re-arms the unavailable-edge log for the NEXT time this provider goes unavailable.

            // Structural cadence floor (F4 fix, T226 review; see ICadenceFlooredContextProvider's
            // own remarks) — the ONE place SegmentCadenceMinutes is actually consumed, so this is
            // where a provider-declared floor is enforced regardless of how a value reached this
            // pipeline (a live PUT, which SettingValidator's own write-time range already guards, OR
            // an appsettings.json/env override, which never passes through that validator at all). A
            // provider that implements no such floor is unaffected — Math.Max against 1 is a no-op
            // ahead of ComputeSlot's own identical floor-divide clamp below.
            var minimumSegmentCadenceMinutes = provider is ICadenceFlooredContextProvider floored
                ? floored.MinimumSegmentCadenceMinutes
                : 1;
            var slot = ComputeSlot(now, Math.Max(settings.SegmentCadenceMinutes, minimumSegmentCadenceMinutes));
            await EnsureFetchedAsync(provider, state, slot, ct).ConfigureAwait(false);

            if (state.Content is not { } content || content.FreshUntil <= now)
            {
                // LogSkipOnce is itself idempotent per (provider, slot) — a null Content here means
                // EnsureFetchedAsync already logged the real cause (threw/no content) this slot, so
                // this call is a harmless no-op rather than a second, overwriting line.
                LogSkipOnce(state, slot, provider.Key, "stale");
                continue;
            }

            if (content.Facts.Count == 0)
                continue; // "nothing to say this fetch" (ContextContent's own doc) — not a failure, no log.

            if (!state.TryMarkSegmentDelivered(slot))
                continue; // Already handed off this slot — never enqueue the same content twice.

            // F125.2/F125.3 vend-time selection: a rotating window through the airable list, joined
            // here — the provider no longer pre-joins (see ContextContent's own remarks). Gated behind
            // TryMarkSegmentDelivered above, not before it, so two calls landing in the same slot can
            // never both advance the rotation cursor for content only one of them actually enqueues.
            // Never null in practice — content.Facts.Count == 0 already continued above, and
            // TakeSegmentWindow only returns null for an empty list — but degrades to a silent skip
            // rather than a thrown exception if that invariant is ever violated.
            if (state.TakeSegmentWindow(content.Facts, SegmentWindowFacts, FactSeparator) is not { } window)
                continue;

            // F125.5: names the chosen window and the aired-set size, never the facts' own text
            // (F108.3 forbids echoing a provider's own content into a log line).
            logger.LogInformation(
                "Context provider {ProviderKey} vended segment facts starting at index {WindowStart} " +
                "(aired-set size {AiredSetSize} of {TotalFacts}).",
                provider.Key, window.WindowStart, window.AiredSetSize, content.Facts.Count);

            due.Add(new DueContextSegment(provider.Key, new ContextSegmentFacts(window.Joined, content.FreshUntil)));
        }

        return due;
    }

    /// <summary>
    /// The patter lane's pull (SPEC F107.5, STORY-298): the first enabled provider whose cached
    /// content is fresh, carries at least one airable <see cref="ContextContent.Facts"/> entry not
    /// yet vended to patter today, and has not already been vended for its current patter-cadence slot
    /// — at most one, since a break's prompt carries at most one context line. The returned fact is
    /// selected HERE, at vend time (SPEC F125.2/F125.3): the first not-yet-aired fact, in list order —
    /// once every fact has aired, this provider is skipped rather than repeating one (patter is
    /// optional color; a repeat is the exact gh-#468 complaint). Never fetches; it only reads whatever
    /// <see cref="TickAsync"/> has already cached, so it costs no I/O and is safe to call from a
    /// synchronous prompt-assembly path.
    ///
    /// <para>
    /// Named <c>TryTake</c>, not a bare getter (F3 fix, T222 review): despite the "current" framing,
    /// this is a CONSUMING read — a fact it returns is marked delivered for its patter-cadence slot
    /// and will not be returned again, so calling it twice for the same due fact yields the fact once
    /// and <see langword="null"/> the second time. A getter-shaped name would invite a caller to poll
    /// it for a peek; there is no non-consuming peek on this class today.
    /// </para>
    /// </summary>
    public ContextPatterFact? TryTakeDuePatterFact()
    {
        var now = timeProvider.GetUtcNow();

        foreach (var provider in providers)
        {
            var settings = settingsProvider.For(provider.Key);
            if (!settings.Enabled)
                continue;

            // Mirrors the Enabled check immediately above, silently — no log call here (F2/F3
            // interaction fix, T227 re-review): TickAsync already owns the ONE edge-triggered
            // Information line for this cause (see the class remarks); this gate exists purely so
            // this lane stops vending the instant a provider self-reports unavailable, whether or
            // not TickAsync has run since — it never depended on TickAsync's own content-clearing to
            // be correct, only to be prompt.
            if (provider is ISelfGatingContextProvider gated && !gated.IsAvailable)
                continue;

            var state = states[provider.Key];
            if (state.Content is not { } content || content.FreshUntil <= now)
                continue;

            if (content.Facts.Count == 0)
                continue; // "nothing to say this fetch" (ContextContent's own doc).

            var patterSlot = ComputeSlot(now, settings.PatterCadenceMinutes);
            if (!state.TryMarkPatterDelivered(patterSlot))
                continue;

            // F125.2/F125.3 vend-time selection: the first not-yet-aired fact, in list order. Gated
            // behind TryMarkPatterDelivered above, not before it (see ProviderState.TryTakePatterFact's
            // own remarks) — a concurrent duplicate call landing in the same slot can never consume a
            // rotation pick for a fact only one of them actually returns.
            if (state.TryTakePatterFact(content.Facts) is not { } picked)
                continue; // Exhausted (F125.3) — the slot is skipped, never forced to repeat.

            // F125.5: names the chosen fact's index and the aired-set size, never the fact's own text
            // (F108.3 forbids echoing a provider's own content into a log line).
            logger.LogInformation(
                "Context provider {ProviderKey} vended patter fact index {FactIndex} " +
                "(aired-set size {AiredSetSize} of {TotalFacts}).",
                provider.Key, picked.Index, picked.AiredSetSize, content.Facts.Count);

            return new ContextPatterFact(provider.Key, picked.Fact);
        }

        return null;
    }

    /// <summary>Attempts exactly one fetch per cadence slot; a null return, a thrown exception, and a
    /// successful result are all recorded so the SAME slot never retries.</summary>
    async Task EnsureFetchedAsync(IContextProvider provider, ProviderState state, long slot, CancellationToken ct)
    {
        if (!state.TryBeginFetch(slot))
            return;

        try
        {
            var content = await provider.FetchAsync(ct).ConfigureAwait(false);

            if (content is null)
            {
                LogSkipOnce(state, slot, provider.Key, "fetch returned no content");
                return;
            }

            // The fencing gate's sanitizing chokepoint (T228, carried forward from the T224/T225
            // reviews; see ContextFactSanitizer's own remarks for why THIS call site, not each
            // provider): every provider's raw ContextContent is neutralized here, once, before ever
            // reaching the cache both TickAsync and TryTakeDuePatterFact read from — so no present or
            // future provider (and no future consumer of ProviderState.Content) can bypass it by
            // forgetting to call it themselves.
            //
            // Deliberately INSIDE this same try (F4 fix, T227/T228 review): ContextContent validates
            // nothing at construction time, so a hostile/broken provider handing back a null
            // Facts list — despite its own `IReadOnlyList<string>`, never `IReadOnlyList<string>?`,
            // contract — makes Sanitize throw. That must degrade to skip-never-silence exactly like a
            // thrown FetchAsync (the catch below), never escape TickAsync/TryTakeDuePatterFact
            // uncaught — moving this call outside the guard, as it stood before this fix, is exactly
            // what let it.
            state.CommitContent(Sanitize(content));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancellation, not a provider fault — never skip-never-silence input.
        }
        catch (Exception ex)
        {
            // F107.1's throw posture (IContextProvider.FetchAsync's own doc): a thrown exception is
            // ordinary skip-never-silence input, same as null. Only the exception's TYPE identifies
            // the cause — never ex.Message, which a provider could have populated with the very
            // provider-authored content F108.3 forbids echoing into a log line.
            LogSkipOnce(state, slot, provider.Key, $"fetch threw {ex.GetType().Name}");
        }
    }

    /// <summary>Applies <see cref="ContextFactSanitizer.Sanitize"/> to every fact a provider's
    /// <see cref="ContextContent"/> carries, in order, dropping any fact that sanitizes down to blank.
    /// A fact that is nothing but control characters/whitespace is "nothing to say" for THAT fact —
    /// the same shape <see cref="ContextContent"/>'s own contract already treats an empty list as —
    /// surviving as a phantom blank list entry would otherwise show up as a stray separator in the
    /// segment lane's own vend-time join (<see cref="FactSeparator"/>).</summary>
    static ContextContent Sanitize(ContextContent content) => content with
    {
        Facts = content.Facts
            .Select(ContextFactSanitizer.Sanitize)
            .Where(fact => !string.IsNullOrWhiteSpace(fact))
            .ToList(),
    };

    /// <summary>Logs at most one Information line per (provider, cadence slot) — the first cause
    /// observed in a slot wins; every later skip evaluation in that same slot is silent.</summary>
    void LogSkipOnce(ProviderState state, long slot, string key, string cause)
    {
        if (!state.TryLogOnce(slot))
            return;

        logger.LogInformation(
            "Context provider {ProviderKey} produced no output this cadence slot: {Cause}.", key, cause);
    }

    /// <summary>Floor-divides the current instant into a fixed-width cadence bucket; a
    /// non-positive cadence clamps to one minute so a misconfigured provider still buckets instead of
    /// dividing by zero.</summary>
    static long ComputeSlot(DateTimeOffset now, int cadenceMinutes)
    {
        var cadenceTicks = TimeSpan.FromMinutes(Math.Max(cadenceMinutes, 1)).Ticks;
        return now.UtcTicks / cadenceTicks;
    }

    /// <summary>
    /// Per-provider mutable cadence-slot bookkeeping. THREAD-SAFE BY ITSELF (F4 fix, T222 review):
    /// <see cref="ContextPipeline.TickAsync"/> (ticker thread) and
    /// <see cref="ContextPipeline.TryTakeDuePatterFact"/> (copywriter thread) may call into the SAME
    /// instance concurrently in production, and a bare <c>long?</c> read/write is not atomic — every
    /// field here is therefore only ever touched from inside <see cref="gate"/>, via the methods
    /// below. Callers (the outer <see cref="ContextPipeline"/>) never lock externally and never read
    /// or write a field directly.
    /// </summary>
    sealed class ProviderState
    {
        readonly object gate = new();

        /// <summary>The slot index of the most recent fetch ATTEMPT (success, null, or throw).</summary>
        long? fetchedSlot;

        /// <summary>The most recent successful fetch's content, cleared on every new-slot attempt
        /// until (if) that attempt succeeds.</summary>
        ContextContent? content;

        /// <summary>The slot index of the most recent per-slot skip-cause Information line
        /// (<see cref="TryLogOnce"/>) — governs the "stale"/"threw"/"no content" causes, never the
        /// disabled cause (that one is edge-triggered via <see cref="disabledLogged"/> instead).</summary>
        long? loggedSlot;

        /// <summary>The segment-cadence slot index already handed off via <see cref="ContextPipeline.TickAsync"/>.</summary>
        long? deliveredSegmentSlot;

        /// <summary>The patter-cadence slot index already vended via <see cref="ContextPipeline.TryTakeDuePatterFact"/>.</summary>
        long? deliveredPatterSlot;

        /// <summary>Whether the disabled cause has already been logged for the CURRENT disabled
        /// streak (F7) — cleared the next time this provider is observed enabled, so the following
        /// disable logs exactly once again.</summary>
        bool disabledLogged;

        /// <summary>Whether the self-gated-unavailable cause (<see cref="ISelfGatingContextProvider"/>,
        /// F2) has already been logged for the CURRENT unavailable streak — cleared the next time this
        /// provider is observed available. Tracked separately from <see cref="disabledLogged"/> so the
        /// "disabled" and "misconfigured" edges never share or clobber one another's state.</summary>
        bool unavailableLogged;

        /// <summary>Indices into the current <see cref="content"/>'s <see cref="ContextContent.Facts"/>
        /// already vended to the patter lane (SPEC F125.3/F125.4) — reset whenever content is
        /// (re)committed or cleared, so a fresh fetch starts patter rotation over from the top, and a
        /// restart forgets it entirely (F125.4's day-scoped, in-memory ruling).</summary>
        HashSet<int> patterAired = [];

        /// <summary>Every distinct index that has appeared in at least one segment window so far this
        /// content generation — observability only (F125.5's "aired-set size" for the segment lane's
        /// own vend log line). The segment lane's actual selection is driven by
        /// <see cref="segmentWindowCursor"/> alone and is never gated by this set: SPEC F125.3 has the
        /// segment lane WRAP rather than exhaust, so unlike <see cref="patterAired"/> this set does not
        /// block a re-selection once it covers every index.</summary>
        HashSet<int> segmentAired = [];

        /// <summary>The next segment window's starting index into <see cref="content"/>'s
        /// <see cref="ContextContent.Facts"/> (SPEC F125.3) — advances by the window's own size after
        /// every vend, wrapping modulo the list's length, and resets to zero on the same edges as
        /// <see cref="patterAired"/>.</summary>
        int segmentWindowCursor;

        /// <summary>The <see cref="ContextContent.FreshUntil"/> of the last content this instance
        /// actually COMMITTED (SPEC F125.4's "FreshUntil roll" reset trigger) — tracked separately
        /// from <see cref="content"/> itself because <see cref="TryBeginFetch"/> nulls
        /// <see cref="content"/> out at the START of every new slot's fetch attempt, before
        /// <see cref="CommitContent"/> ever runs; comparing against a null <see cref="content"/> there
        /// would make every single commit look like the first one ever, resetting rotation on every
        /// re-fetch regardless of whether the content actually changed. <see langword="null"/> only
        /// before this provider's first-ever successful commit.</summary>
        DateTimeOffset? lastCommittedFreshUntil;

        /// <summary>The <see cref="ContextContent.Facts"/> count of the last content this instance
        /// actually committed — a SECOND, independent reset trigger alongside
        /// <see cref="lastCommittedFreshUntil"/> (review finding, O1): a provider is free to keep the
        /// SAME <see cref="ContextContent.FreshUntil"/> across a fetch whose airable list nonetheless
        /// shrank or grew (e.g. the tone gate removing a different count of facts on a re-fetch that
        /// otherwise reused the day's cached FreshUntil). Without this, <see cref="segmentAired"/>/
        /// <see cref="patterAired"/> could keep indices from the OLD, longer list around after the
        /// list shrank — never a crash (every selection already indexes modulo the CURRENT list's
        /// length), but <see cref="segmentAired"/>'s own count could then exceed the current list's
        /// count, making the F125.5 "aired-set size" log line report something larger than the total
        /// facts count it is reported alongside. Treating a shape change as its own new generation,
        /// the same way a FreshUntil roll is, keeps that count always bounded by the current list — a
        /// stronger fix than clamping the log line alone, which would leave the stale indices sitting
        /// in the sets even though they no longer correspond to anything. What this trigger does NOT
        /// catch: a same-COUNT content swap (the FreshUntil unchanged, N facts replaced by a
        /// DIFFERENT N facts) silently remaps aired indices onto the new facts, which can skip or
        /// repeat one fact for at most one generation — accepted, since it is unreachable for both
        /// shipped providers (Weather re-rolls FreshUntil on every single fetch; History's day file is
        /// a cache hit for the entire day, so its content is byte-identical, not merely same-sized,
        /// across every re-fetch) and smaller in impact than the restart-forgets imprecision F125.4
        /// already ratifies.</summary>
        int? lastCommittedFactCount;

        /// <summary>The most recently committed fetch content, or <see langword="null"/> if this
        /// slot's attempt has not yet succeeded (or has not been attempted). Freshness against
        /// <see cref="ContextContent.FreshUntil"/> is the caller's own comparison — the record itself
        /// is immutable once handed out, so no lock is needed to read its properties after this
        /// getter returns.</summary>
        public ContextContent? Content
        {
            get { lock (gate) { return content; } }
        }

        /// <summary>Reserves <paramref name="slot"/> as the current fetch attempt and clears any
        /// previously cached content, unless this exact slot was already reserved (fetch-once-per-
        /// slot) — in which case this is a no-op. Returns whether the caller should proceed to
        /// fetch.</summary>
        public bool TryBeginFetch(long slot)
        {
            lock (gate)
            {
                if (fetchedSlot == slot)
                    return false;

                fetchedSlot = slot;
                content = null;
                return true;
            }
        }

        /// <summary>Commits a successful fetch's result. Resets rotation only on a genuine new content
        /// GENERATION — either the <see cref="ContextContent.FreshUntil"/> ROLLING forward (SPEC
        /// F125.4's own wording) or the airable list's own COUNT changing (O1) — NOT on every call. A
        /// provider whose fetch cadence is narrower than its own content's shelf life
        /// (<c>HistoryContextProvider</c>'s 4-hour default cadence against an all-day
        /// <see cref="ContextContent.FreshUntil"/>) re-fetches the SAME cached day's content many
        /// times before it ever rolls over — the day file is unchanged, so the fetch returns the same
        /// facts with the same <see cref="ContextContent.FreshUntil"/> every time. Resetting on every
        /// one of those re-fetches would make rotation start over before it ever had a chance to vary
        /// within the day, defeating the whole point of F125.2. Any fetch whose FreshUntil differs
        /// from what was cached before it (later, the ordinary "the day rolled over" case; or,
        /// defensively, earlier) is treated as a new content generation and resets — and so is any
        /// fetch whose Facts count differs from before, even when FreshUntil happens to stay the same
        /// (see <see cref="lastCommittedFactCount"/>'s own remarks). Call only after
        /// <see cref="TryBeginFetch"/> returned <see langword="true"/> for the same slot.</summary>
        public void CommitContent(ContextContent fetchedContent)
        {
            lock (gate)
            {
                var isNewGeneration =
                    lastCommittedFreshUntil is null
                    || fetchedContent.FreshUntil != lastCommittedFreshUntil
                    || fetchedContent.Facts.Count != lastCommittedFactCount;

                if (isNewGeneration)
                    ResetRotation();

                lastCommittedFreshUntil = fetchedContent.FreshUntil;
                lastCommittedFactCount = fetchedContent.Facts.Count;
                content = fetchedContent;
            }
        }

        /// <summary>Clears any cached content (F2/F3 interaction fix, T227 re-review) — called on the
        /// self-gated-unavailable edge (<see cref="NoteAvailable"/> returning <see langword="true"/>),
        /// the moment a provider's last-fetched content stops being trustworthy. Nothing repopulates
        /// content while a provider stays unavailable (<c>EnsureFetchedAsync</c> is never reached for
        /// it), so clearing once, on the edge, is enough for the whole unavailable streak — there is
        /// no need to call this again on every subsequent unavailable tick. Rotation resets alongside
        /// content for the same reason (SPEC F125.4): the content this provider serves once it comes
        /// back is untrusted as a continuation of whatever rotation was mid-cycle before it went dark,
        /// so this forces the NEXT successful <see cref="CommitContent"/> to treat itself as a new
        /// generation even in the vanishingly unlikely case its FreshUntil happens to coincide with
        /// what was cached before the outage.</summary>
        public void ClearContent()
        {
            lock (gate)
            {
                content = null;
                lastCommittedFreshUntil = null;
                lastCommittedFactCount = null;
                ResetRotation();
            }
        }

        /// <summary>Call only from inside <see cref="gate"/> (see <see cref="CommitContent"/>/
        /// <see cref="ClearContent"/>, its only two callers).</summary>
        void ResetRotation()
        {
            patterAired = [];
            segmentAired = [];
            segmentWindowCursor = 0;
        }

        /// <summary>Patter's vend-time pick (SPEC F125.3): the first entry in <paramref name="facts"/>
        /// not yet aired this content generation, in list order — never repeats. Returns
        /// <see langword="null"/> once every fact has aired. Mutates <see cref="patterAired"/> only
        /// when a fact is actually returned, so a caller that ends up discarding the result (e.g. the
        /// slot-delivery gate rejecting a concurrent duplicate call one layer up) never burns a pick
        /// for content nobody receives.</summary>
        public (int Index, string Fact, int AiredSetSize)? TryTakePatterFact(IReadOnlyList<string> facts)
        {
            lock (gate)
            {
                for (var index = 0; index < facts.Count; index++)
                {
                    if (patterAired.Contains(index))
                        continue;

                    patterAired.Add(index);
                    return (index, facts[index], patterAired.Count);
                }

                return null;
            }
        }

        /// <summary>Segment's vend-time pick (SPEC F125.3): up to <paramref name="windowSize"/>
        /// consecutive facts starting at <see cref="segmentWindowCursor"/>, wrapping modulo
        /// <paramref name="facts"/>'s own length — never exhausts, so this only returns
        /// <see langword="null"/> when <paramref name="facts"/> itself is empty. Joins the window with
        /// <paramref name="separator"/>, advances the cursor by the window's own size (mod the list's
        /// length) so the NEXT vend continues where this one left off, and folds every included index
        /// into <see cref="segmentAired"/> (observability only — see that field's own remarks).</summary>
        public (string Joined, int WindowStart, int AiredSetSize)? TakeSegmentWindow(
            IReadOnlyList<string> facts, int windowSize, string separator)
        {
            lock (gate)
            {
                if (facts.Count == 0)
                    return null;

                var actualWindowSize = Math.Min(windowSize, facts.Count);
                var windowStart = segmentWindowCursor;
                var chosen = new List<string>(actualWindowSize);

                for (var offset = 0; offset < actualWindowSize; offset++)
                {
                    var index = (windowStart + offset) % facts.Count;
                    chosen.Add(facts[index]);
                    segmentAired.Add(index);
                }

                segmentWindowCursor = (windowStart + actualWindowSize) % facts.Count;

                return (string.Join(separator, chosen), windowStart, segmentAired.Count);
            }
        }

        /// <summary>Records that a per-slot skip cause was logged for <paramref name="slot"/>, unless
        /// this exact slot was already logged. Returns whether the caller should actually log (the
        /// first-observed-cause-wins rule).</summary>
        public bool TryLogOnce(long slot)
        {
            lock (gate)
            {
                if (loggedSlot == slot)
                    return false;

                loggedSlot = slot;
                return true;
            }
        }

        /// <summary>Marks <paramref name="slot"/> as handed off to the segment lane, unless it
        /// already was. Returns whether the caller should actually enqueue.</summary>
        public bool TryMarkSegmentDelivered(long slot)
        {
            lock (gate)
            {
                if (deliveredSegmentSlot == slot)
                    return false;

                deliveredSegmentSlot = slot;
                return true;
            }
        }

        /// <summary>Marks <paramref name="slot"/> as vended to the patter lane, unless it already
        /// was. Returns whether the caller should actually vend.</summary>
        public bool TryMarkPatterDelivered(long slot)
        {
            lock (gate)
            {
                if (deliveredPatterSlot == slot)
                    return false;

                deliveredPatterSlot = slot;
                return true;
            }
        }

        /// <summary>
        /// Feeds this tick's enabled/disabled reading and reports whether the disabled cause should
        /// be logged THIS call (F7, the gh-#338 edge-trigger precedent). Observing <c>enabled: true</c>
        /// always returns <see langword="false"/> and re-arms the edge for the next disable;
        /// observing <c>enabled: false</c> returns <see langword="true"/> exactly once per disabled
        /// streak — every later disabled tick returns <see langword="false"/>, however long the
        /// provider stays off, until it is next observed enabled.
        /// </summary>
        public bool NoteEnabled(bool enabled)
        {
            lock (gate)
            {
                if (enabled)
                {
                    disabledLogged = false;
                    return false;
                }

                if (disabledLogged)
                    return false;

                disabledLogged = true;
                return true;
            }
        }

        /// <summary>
        /// Feeds this tick's <see cref="ISelfGatingContextProvider.IsAvailable"/> reading and reports
        /// whether the "misconfigured" cause should be logged THIS call — the exact mirror of
        /// <see cref="NoteEnabled"/>, on its own independent edge (F2, T227 review).
        /// </summary>
        public bool NoteAvailable(bool available)
        {
            lock (gate)
            {
                if (available)
                {
                    unavailableLogged = false;
                    return false;
                }

                if (unavailableLogged)
                    return false;

                unavailableLogged = true;
                return true;
            }
        }
    }
}
