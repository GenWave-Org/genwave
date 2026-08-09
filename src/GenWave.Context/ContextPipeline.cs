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
/// <see cref="ContextProviderSettings.SegmentCadenceMinutes"/> minutes (floor-divided from the
/// <see cref="TimeProvider"/> epoch, so every instance buckets identically without needing to agree
/// on a start time). A provider's <see cref="IContextProvider.FetchAsync"/> is invoked AT MOST ONCE
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
    /// cached content is fresh, carries non-blank <see cref="ContextContent.SegmentFacts"/>, and has
    /// not already been handed off for its current slot. Safe to call more often than any provider's
    /// cadence — the T226 Host ticker is the one, dumb, fixed-interval caller.
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

            var slot = ComputeSlot(now, settings.SegmentCadenceMinutes);
            await EnsureFetchedAsync(provider, state, slot, ct).ConfigureAwait(false);

            if (state.Content is not { } content || content.FreshUntil <= now)
            {
                // LogSkipOnce is itself idempotent per (provider, slot) — a null Content here means
                // EnsureFetchedAsync already logged the real cause (threw/no content) this slot, so
                // this call is a harmless no-op rather than a second, overwriting line.
                LogSkipOnce(state, slot, provider.Key, "stale");
                continue;
            }

            if (string.IsNullOrWhiteSpace(content.SegmentFacts))
                continue; // "no segment lane this fetch" (ContextContent's own doc) — not a failure, no log.

            if (!state.TryMarkSegmentDelivered(slot))
                continue; // Already handed off this slot — never enqueue the same content twice.

            due.Add(new DueContextSegment(provider.Key, content));
        }

        return due;
    }

    /// <summary>
    /// The patter lane's pull (SPEC F107.5, STORY-298): the first enabled provider whose cached
    /// content is fresh, carries a non-blank <see cref="ContextContent.PatterFact"/>, and has not
    /// already been vended for its current patter-cadence slot — at most one, since a break's prompt
    /// carries at most one context line. Never fetches; it only reads whatever <see cref="TickAsync"/>
    /// has already cached, so it costs no I/O and is safe to call from a synchronous prompt-assembly
    /// path.
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

            if (content.PatterFact is not { } fact || string.IsNullOrWhiteSpace(fact))
                continue;

            var patterSlot = ComputeSlot(now, settings.PatterCadenceMinutes);
            if (!state.TryMarkPatterDelivered(patterSlot))
                continue;

            return new ContextPatterFact(provider.Key, fact);
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
            // SegmentFacts — despite its own `string`, never `string?`, contract — makes Sanitize
            // throw ArgumentNullException. That must degrade to skip-never-silence exactly like a
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

    /// <summary>Applies <see cref="ContextFactSanitizer.Sanitize"/> to every fact string a provider's
    /// <see cref="ContextContent"/> carries — <see cref="ContextContent.PatterFact"/> is nullable and
    /// preserved as null rather than sanitized into a spurious empty string.</summary>
    static ContextContent Sanitize(ContextContent content) => content with
    {
        SegmentFacts = ContextFactSanitizer.Sanitize(content.SegmentFacts),
        PatterFact = content.PatterFact is { } fact ? ContextFactSanitizer.Sanitize(fact) : null,
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

        /// <summary>Commits a successful fetch's result. Call only after <see cref="TryBeginFetch"/>
        /// returned <see langword="true"/> for the same slot.</summary>
        public void CommitContent(ContextContent fetchedContent)
        {
            lock (gate)
            {
                content = fetchedContent;
            }
        }

        /// <summary>Clears any cached content (F2/F3 interaction fix, T227 re-review) — called on the
        /// self-gated-unavailable edge (<see cref="NoteAvailable"/> returning <see langword="true"/>),
        /// the moment a provider's last-fetched content stops being trustworthy. Nothing repopulates
        /// content while a provider stays unavailable (<c>EnsureFetchedAsync</c> is never reached for
        /// it), so clearing once, on the edge, is enough for the whole unavailable streak — there is
        /// no need to call this again on every subsequent unavailable tick.</summary>
        public void ClearContent()
        {
            lock (gate)
            {
                content = null;
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
