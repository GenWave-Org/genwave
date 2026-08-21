namespace GenWave.Tts;

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Core.Logging;

public sealed class TtsSegmentSource(
    ISegmentCopyWriter copyWriter,
    ITtsSynthesizer synthesizer,
    ILoudnessAnalyzer analyzer,
    ICueAnalyzer cueAnalyzer,
    SpeechCorrectionProvider corrections,
    ActivePersonaCorrectionsCache personaCorrections,
    PronunciationRuleProvider pronunciations,
    ActivePersonaPronunciationRulesCache personaPronunciations,
    ActivePersonaPaceCache personaPace,
    IOptionsMonitor<TtsOptions> options,
    ILogger<TtsSegmentSource> logger,
    IStationEventSink? events = null) : ITtsSegmentSource
{
    // SegmentGenerated publish seam (gitea-#246); no-op unless the host binds a real sink.
    readonly IStationEventSink events = events ?? NoOpStationEventSink.Instance;
    // Fresh-per-airing (LLM-authored) blurb audio lands here instead of the station's forever-cache
    // root, so it can be swept without touching templated kinds' stable (text,voice) cache (F34.6).
    // SignOff/SignOn (F92.4, F92.5) never reach this far on a template-fallback miss at all — see the
    // WARN+null guard right after copyWriter.WriteAsync below, which drops that render before a cache
    // path is even chosen — so by the time isBlurb is computed, copy.FreshPerAiring is the single
    // correct test for every kind reaching this point, handoff kinds included.
    const string BlurbsDirName = "blurbs";

    // SPEC F100.2 (STORY-258, PLAN T143): the render-outcome fact's own field values, shared by
    // both LogRenderOutcome call sites below so the two can never drift into different spellings
    // of the same concept.
    const string OutcomeSuccess = "success";
    const string OutcomeFailure = "failure";
    const string NoCause = "n/a";

    readonly ConcurrentDictionary<string, CuePoints?> cueCache = new();

    // Bumped whenever the persona/station MERGE ALGORITHM changes — never on a rule-CONTENT change,
    // which corrections.ContentHash / personaCorrections.ContentHash / pronunciations.ContentHash /
    // personaPronunciations.ContentHash below already cover. The T136 precedence flip (F97.4,
    // station-wins -> card-wins) is exactly this case: for an UNCHANGED (station rules, card rules)
    // pair the two content fingerprints are byte-identical before and after the flip, yet the
    // rendered audio is not — SpeechCorrectionProvider.BuildMerged/PronunciationRuleProvider.BuildMerged
    // now resolve the very same overlap differently (both delegate to the shared
    // PersonaOverStationMerge, so one version term covers both rule families — see its own remarks).
    // Without a term that reacts to the ALGORITHM, not just the rules, an evergreen
    // (FreshPerAiring:false) StationId/LeadIn/BackAnnounce clip already sitting in the named-volume
    // cache would keep airing the pre-flip pronunciation forever, since RenderAsync's file-exists
    // short-circuit below never reaches the synthesizer (and so never reaches either BuildMerged)
    // again for that key. Bump this string on any future merge-policy change. Internal, not private:
    // the ScenarioMergePolicyVersionIsPartOfTheCacheKey spec (Story005) reads it to pin the hash
    // FORMULA — not this value — rather than duplicating it as a literal that could drift.
    // "+gh541": the speakability flatten (SpeechText.FlattenForSpeech) changed rendered audio for
    // identical copy + fingerprints — the same no-fingerprint-moves shape as the merge flip — so
    // the bump re-keys every evergreen clip once and the flattened render replaces the cached
    // punctuation pauses instead of them airing forever.
    internal const string MergePolicyVersion = "f97.4+gh541";

    public async Task<MediaItem?> RenderAsync(SegmentRequest request, CancellationToken ct)
    {
        try
        {
            // Read fresh per render — not a boot-frozen field — so Tts:BlurbRetentionHours
            // (SPEC F44.2, closes gitea-#197) is live for SweepBlurbs below. CacheRoot/Format are not
            // operator-editable (deployment topology, F44.4), so reading them from CurrentValue
            // instead of a frozen snapshot changes nothing observable for them.
            var cfg = options.CurrentValue;
            var copy = await copyWriter.WriteAsync(request, ct);

            // Design ruling, spec-cited (T123 review finding, extended to ContextSegment at T224):
            // a handoff piece OR a context segment must NEVER air non-LLM-authored copy. F92.4's
            // ladder is "two-piece -> whichever piece rendered -> clean cut" — there is no "templated
            // piece" rung — and F92.5/F107.6 both state this copy IS an LLM-authored blurb, full
            // stop: for a handoff, the alternative is silently wrong ceremony phrasing; for a context
            // segment, the alternative is PatterTemplateRenderer's inert placeholder ("Here's
            // something worth knowing") standing in for actual facts, which defeats the entire point
            // of a context provider (never airable filler, SPEC F107.6). copy.FreshPerAiring false
            // here means every writer in the chain missed: LlmCopyWriter's own FOUR degrade paths
            // (disabled endpoint, timeout/non-2xx/connect, empty-or-over-length after cleanup, and —
            // as of PLAN T332, SPEC F138.2-F138.4 — the truth-gate ladder exhausting its one re-ask)
            // AND DegradationGatedCopyWriter routing straight to TemplateCopyWriter — unconditionally
            // in Hard mode, or off an unclaimed Soft cadence slot — bypassing LlmCopyWriter entirely.
            // Every one of those returns template copy rather than throwing (ISegmentCopyWriter's own
            // never-throws contract), which is exactly why PatterTemplateRenderer still needs correct
            // SignOff/SignOn/ContextSegment arms — they just must never reach air for these three
            // kinds specifically (LeadIn/BackAnnounce carry no such guard below — their own template
            // rung DOES reach air on a miss, truth-gate exhaustion included, since neither kind's
            // FIXED PROSE states a weekday/daypart claim on its own; see PatterTemplateRenderer.Expand's
            // own LeadIn/BackAnnounce arms — the interpolated track title/artist is the one part of
            // that text NOT gate-checked either way, since a template render never reaches this ladder
            // to begin with). One WARN, then null:
            // ITtsSegmentSource already allows null-never-throws, and the Orchestrator's own drain arm
            // treats a null render exactly like F92.4's "whichever piece rendered airs (else clean
            // cut)"/F107.6's skip-never-silence posture.
            if (request.Kind is SegmentKind.SignOff or SegmentKind.SignOn or SegmentKind.ContextSegment
                && !copy.FreshPerAiring)
            {
                logger.LogWarning(
                    "Copy for {Kind} on station {StationId} was not LLM-authored (writer degraded to " +
                    "template) — dropping this segment rather than airing non-LLM-authored copy " +
                    "(SPEC F92.4, F92.5, F107.6)",
                    request.Kind, request.StationId);
                return null;
            }

            // corrections.ContentHash (station rules) AND personaCorrections.ContentHash (the
            // active persona card's rules, SPEC F71.7) both fold into the cache key (SPEC F68.5) so
            // EITHER a corrections rebuild (PUT /api/settings), a card edit, or a
            // Station:Persona:ActiveId switch re-keys every subsequent cache lookup: the very next
            // render of the same (text, voice, station) misses, falls through to
            // synthesizer.SynthesizeAsync below (NormalizingTtsSynthesizer — the only place either
            // set of corrections actually applies, via SpeechCorrectionProvider.BuildMerged's
            // persona-over-station merge, F97.4), and lands under a new hash. This class never reads a
            // correction rule itself, only the two fingerprints saying "these are the rules in
            // effect on each side of the merge". MergePolicyVersion (see its own remarks above)
            // folds in alongside them for the third, orthogonal reason a render can change without
            // either fingerprint moving: the ALGORITHM that resolves an overlap between the two
            // rule sets changed, not the rules themselves.
            //
            // Ordering matters: RefreshIfStaleAsync is awaited BEFORE computing the hash below —
            // NOT left for NormalizingTtsSynthesizer's own call inside SynthesizeAsync to discover
            // first — so the key and the eventual render read the SAME generation of
            // personaCorrections in the common case (this cache is a DI singleton; its own refresh
            // is idempotent and gated, so NormalizingTtsSynthesizer's later call is just a fast
            // already-fresh no-op). Reversing this order would let the key capture the PRE-refresh
            // snapshot while the render — on a cache miss — applies the POST-refresh one: a fresh
            // synthesis would then land under a hash that no longer matches what was actually
            // spoken, and the file would sit orphaned until the next render recomputes with the new
            // snapshot and re-hits it (self-healing next render — same accepted mid-render race
            // TtsSegmentSource already tolerates elsewhere, just moved to a different trigger).
            //
            // A deterministic content fingerprint — NOT SpeechCorrectionProvider.Version (a
            // process-local counter that resets to 0 at every construction) — is required here: the
            // TTS cache directory is a named Docker volume and its files are never swept on their
            // own (only blurbsDir entries are, see SweepBlurbs below), so it outlives any container
            // redeploy. A counter-based key would let a fresh process's version=0 collide with
            // orphaned pre-redeploy entries and serve stale pronunciation again; the same rules
            // always fold to the same fingerprint across restarts, and changed rules always fold to
            // a new one, so a redeploy can never accidentally resurrect a stale cache entry.
            // Without SOME such term here, an evergreen StationId/LeadIn/BackAnnounce clip
            // (FreshPerAiring:false, never GC'd) would keep airing the OLD pronunciation forever.
            // The file at the stale hash is simply orphaned, never rewritten or deleted — accepted
            // disk cost on the evergreen stationDir cache (a named volume with no retention sweep of
            // its own): correctness on the very next spoken line matters more here than reclaiming a
            // few stale audio files.
            //
            // Staleness bound inherited, honestly: personaCorrections.ContentHash can itself lag a
            // real card edit/persona switch by up to ActivePersonaCorrectionsCache.StalenessBound
            // (its own refresh is a bounded poll, not an instant subscription — see its class
            // remarks) — the cache key can never be MORE current than the rules it is keying on. A
            // station-only deployment (no active persona at all) is unaffected: personaCorrections
            // always folds to its own stable "no card corrections" sentinel there, so this term
            // never varies for a station running with no persona feature in play.
            await personaCorrections.RefreshIfStaleAsync(ct);

            // Pronunciation rules (SPEC F97.3, F97.6): resolved HERE, at the segment source, via ONE
            // ambient-persona read (personaPronunciations.Current, refreshed just above) — never
            // re-read from an ambient accessor deeper in the pipeline (ARCHITECTURE.md "Carrying
            // persona facts to the engine"; see PronunciationRuleSet/TtsRenderContext.Rules).
            // SegmentRequest carries no persona identity of its own, so this is not "the persona this
            // request was authored for" resolved by name — it is the SAME posture
            // ActivePersonaCorrectionsCache already ships for corrections: whichever persona is
            // active at the moment of this one read is who this render carries forward, immutably,
            // on TtsRenderContext.Rules to both Kokoro request builders, so a slow render can never
            // observe a LATER persona flip mid-flight. pronunciations.ContentHash
            // (station) and personaPronunciations.ContentHash (card) fold into the cache key exactly
            // like the corrections pair above and for the identical reason: an edited rule must
            // re-key an evergreen cached clip rather than let it keep airing the old pronunciation.
            await personaPronunciations.RefreshIfStaleAsync(ct);
            // PronunciationRuleResolver.ResolveForRender (T274 review finding F3) is the ONE seam
            // both this render and the admin preview (TtsPreviewController) call — no candidate
            // layer here (that is a preview-only concept), so this is exactly the plain
            // station∪persona merge, byte-identical to before that seam existed.
            var contextRules = PronunciationRuleResolver.ResolveForRender(pronunciations.Current, personaPronunciations.Current);

            // Speaking pace (SPEC F98.1-F98.3, PLAN T140): resolved via the SAME "one
            // ambient-persona read" discipline Rules just used above — never re-read from an
            // ambient accessor deeper in the pipeline (ARCHITECTURE.md "Carrying persona facts to
            // the engine"). personaPace.Current is already validated (TtsPace.Clamp, run inside
            // ActivePersonaPaceCache's own refresh) — safe to fold into the cache key and safe to
            // send to Kokoro as-is, with no further checking here.
            await personaPace.RefreshIfStaleAsync(ct);
            var pace = personaPace.Current;

            var hash = ComputeHash(
                copy.Text, request.Voice, request.StationId, corrections.ContentHash, personaCorrections.ContentHash,
                pronunciations.ContentHash, personaPronunciations.ContentHash, MergePolicyVersion, pace);
            var stationDir = Path.Combine(cfg.CacheRoot, request.StationId);
            // Plain FreshPerAiring is the whole test again (F34.6): the guard above already sent a
            // non-fresh SignOff/SignOn render home before this line, so nothing reaching here needs a
            // second, kind-based override to land in blurbs/ — a genuinely LLM-authored render of ANY
            // kind still does, an evergreen template render of any kind still doesn't.
            var isBlurb = copy.FreshPerAiring;
            var targetDir = isBlurb ? Path.Combine(stationDir, BlurbsDirName) : stationDir;
            var path = Path.Combine(targetDir, $"{hash}.{cfg.Format}");

            var fileExists = File.Exists(path);
            if (!fileExists)
            {
                Directory.CreateDirectory(targetDir);
                // Kind-aware overload (SPEC F70.3, STORY-191): this is the one caller that knows a
                // real SegmentKind — FallbackTtsSynthesizer reads it to consult Tts:EngineByKind.
                // Rules carries the merged pronunciation set resolved above (SPEC F97.6); Pace
                // carries the validated persona rate resolved just above (SPEC F98.2, T140).
                var synthPath = await synthesizer.SynthesizeAsync(
                    new TtsRenderContext(copy.Text, request.Voice, request.Kind) { Rules = contextRules, Pace = pace },
                    ct);
                // A failed Move (destination directory vanished mid-render, a lost race with a
                // concurrent sweep, disk pressure) must never leave the engine's transient write
                // behind as a permanent orphan under CacheRoot's top level, where nothing ever
                // sweeps it — mirrors SafeSegmentAuthor's own all-or-nothing cleanup discipline.
                // The Move failure itself still propagates unchanged to the catch below (WARN +
                // null, F92.4's never-silent posture); this only ensures it never leaves a second,
                // silent failure (an orphaned file) behind it.
                try
                {
                    File.Move(synthPath, path, overwrite: true);
                }
                catch
                {
                    DeleteIfExists(synthPath);
                    throw;
                }
            }

            var loudness = await analyzer.AnalyzeAsync(path, ct);

            CuePoints? cuePoints;
            if (fileExists && cueCache.TryGetValue(hash, out var cached))
            {
                cuePoints = cached;
            }
            else
            {
                cuePoints = await MeasureCueAsync(path, hash, ct);
            }

            // Duration is measured, never fabricated (SPEC F66.1): stamped from the cue analyzer's
            // CueOutSec — same derivation SafeSegmentAuthor.BuildInsert uses for authored segments —
            // and stays null when cue analysis failed (already logged in MeasureCueAsync above).
            // cuePoints covers BOTH the fresh-render and cache-hit paths above, so a cached segment's
            // cached cue points stamp the duration here too.
            var durationMs = cuePoints is not null
                ? (int?)Math.Round(cuePoints.CueOutSec * 1000.0, MidpointRounding.AwayFromZero)
                : null;

            // Opportunistic GC (F34.6): only after a render that actually landed in blurbs/ (fresh
            // copy — the only route left, now that a non-fresh handoff render never reaches this far
            // at all) — templated kinds' forever-cache is never touched. Best-effort; a sweep failure
            // must never fail a render that already succeeded.
            if (isBlurb)
                SweepBlurbs(targetDir, request.StationId);

            // Display title is the station name, NOT the spoken text (issue gitea-#154) — players would
            // otherwise show the whole patter script as the now-playing title. Artist credits the
            // active persona reading the patter when one is active, else the station name (SPEC
            // F39.2, gitea-#212): while a persona is on air it is that persona's voice reading the
            // DJ-spoken kinds (TimeDate, LeadIn, BackAnnounce) alike, so the credit follows it.
            // StationId is the exception (gh-#96): station imaging always arrives with the station's
            // own voice and PersonaName null, so its credit is the station name by construction. No
            // active persona falls back to the gitea-#192/gitea-#172 brand rule unchanged (artist = <Station Name>) —
            // without it every station ID / lead-in / back-announce rendered "Unknown artist" in
            // the admin UI's now-playing and play-history surfaces. This is per-airing state, not
            // cached content: the cache key below never includes PersonaName, so a cache-hit render
            // still carries whichever persona is CURRENTLY active (F39.3).
            // Render succeeded (cache hit or fresh synthesis) — publish before returning (gitea-#246).
            events.Publish(new SegmentGenerated($"tts:{hash}", request.Kind.ToString(), request.Voice));

            // DjName (gh-#259) carries the SPEAKER's persona name for Now Playing attribution —
            // request.PersonaName verbatim, no StationName fallback (unlike the Artist credit line
            // above): a station-voiced segment has no DJ of its own, and the Orchestrator stamps the
            // unit's show persona onto StationId segments itself. Per-airing state, same as Artist —
            // never part of the cache key.
            // SegmentKind (SPEC F113.1, PLAN T220): stamped from this exact render's own request.Kind —
            // the demo-hour instrument reads it back off the AIRED track, never re-derived, so a render
            // that never reaches air (budget-dropped) never carries it into a track-started row at all.
            LogRenderOutcome(request, OutcomeSuccess, NoCause);
            return new MediaItem(
                $"tts:{hash}", path, request.StationName, loudness,
                Artist: request.PersonaName ?? request.StationName, Cue: cuePoints, DurationMs: durationMs,
                DjName: request.PersonaName, SegmentKind: request.Kind);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Genuine caller cancellation — not a completed render either way — there is no
            // outcome to attribute a rate to, so LogRenderOutcome is deliberately not called here
            // (SPEC F100.2). Guarded so this arm no longer catches every OperationCanceledException
            // unconditionally (F1 review finding, PLAN T143 re-review): an unguarded catch here
            // swallowed a Kokoro HttpClient timeout — which throws TaskCanceledException with the
            // CALLER's ct left uncancelled — as a silent null, no WARN, no outcome line, so a hung
            // engine vanished from the render-outcome rate while genuinely failing. Mirrors
            // FallbackTtsSynthesizer.RenderHopAsync's own `when (!ct.IsCancellationRequested)`
            // discriminator for the identical "budget elapsed vs. real cancellation" distinction,
            // inverted here because THIS arm is the one claiming the cancellation, not the one
            // reclassifying it. When the guard fails, control falls through to catch (Exception ex)
            // below — TaskCanceledException derives from OperationCanceledException derives from
            // Exception — so an uncancelled-token OCE is treated as an ordinary render failure.
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TTS render failed for {Kind}/{Voice}", request.Kind, request.Voice);
            LogRenderOutcome(request, OutcomeFailure, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// SPEC F100.2 (STORY-258, PLAN T143): one Information line per completed render — success and
    /// failure alike, not failure alone — naming the PERSONA, not merely the voice the WARN above
    /// already names, so a failure RATE becomes computable per DJ rather than a raw failure count
    /// that says nothing about whether a DJ is actually worse or merely on air more often. One line
    /// per render, not per stage — mirrors <see cref="PronunciationRuleHitReporter"/> and
    /// <see cref="NormalizingTtsSynthesizer.ReportFiredCorrections"/>'s own Information-not-Debug
    /// ground (SPEC F97.5/F100.1, PLAN T142): debug never reaches the fleet log store, so a fact
    /// that exists only there does not exist in the field.
    ///
    /// <paramref name="outcome"/> and <paramref name="cause"/> always ride the SAME message
    /// template regardless of which arm called this — never a different template per outcome — so
    /// success vs. failure is a field value to filter/aggregate on, never something inferred from
    /// which line fired (STORY-258, "the outcome itself is on the line"). <paramref name="cause"/>
    /// is <see cref="NoCause"/> on a success; on a failure it is the exception's own type name
    /// (the "cause class"), read directly off the same exception the WARN above already logs in
    /// full.
    ///
    /// <see cref="SegmentRequest.PersonaName"/> rides through unchanged — never re-derived — the
    /// SAME field the Orchestrator already stamps from its one <c>IActivePersonaAccessor</c> read
    /// (SPEC F39.1). <see langword="null"/> means no persona was active for this segment (station
    /// imaging, gh-#96, is deliberately persona-less): logged as the literal <c>"none"</c> rather
    /// than omitted, so the absence reads as a fact rather than a logging bug (STORY-258 sad path).
    /// Persona-card-authored text is newline-stripped (<see cref="LogSanitize.Strip"/>, the
    /// pronunciation/correction rule-hit family's converged idiom, PLAN T142 — <c>LlmCopyWriter</c>'s
    /// own WARN line is a separate family and still spells this <c>ReplaceLineEndings</c>, untouched
    /// here) so a crafted persona name cannot forge additional log lines (CodeQL
    /// <c>cs/log-forging</c>). Quoted (<c>persona="{PersonaName}"</c>) — unlike <c>Kind</c>/
    /// <c>Outcome</c>/<c>Cause</c>, which are always single tokens — because a persona display name
    /// may legitimately contain spaces ("Rusty Strings"); <c>observability/LABELS.md</c> names
    /// <c>| logfmt</c> as an intended query path, and unquoted logfmt truncates a value at its first
    /// space (F2 review finding, PLAN T143 re-review). Embedded double quotes in the name itself
    /// (e.g. <c>Rusty "The Riff" Strings</c>) are backslash-escaped AFTER <see cref="LogSanitize.Strip"/>
    /// runs, at this call site (round-2 F2 review finding) — a raw embedded quote would close the
    /// wrapping pair early, so a logfmt reader would extract only <c>Rusty </c> and treat
    /// <c>The Riff" Strings</c> as stray, unparsed content. Escaping (not stripping) is deliberate:
    /// it preserves the operator's actual persona name in full rather than silently losing the
    /// quoted portion.
    /// </summary>
    void LogRenderOutcome(SegmentRequest request, string outcome, string cause)
    {
        var personaName = request.PersonaName is { } name
            ? LogSanitize.Strip(name).Replace("\"", "\\\"", StringComparison.Ordinal)
            : "none";
        logger.LogInformation(
            "TTS render outcome: kind={Kind} persona=\"{PersonaName}\" outcome={Outcome} cause={Cause}",
            request.Kind, personaName, outcome, cause);
    }

    /// <summary>
    /// Deletes <paramref name="blurbsDir"/> entries whose last-write time is older than
    /// <see cref="TtsOptions.BlurbRetentionHours"/>. Never reaches outside <paramref name="blurbsDir"/>.
    /// Stops at the first delete failure (locked file, permission denied, lost race with a concurrent
    /// delete) and logs once — the next blurb render retries whatever is left (F34.6, AC4).
    /// </summary>
    void SweepBlurbs(string blurbsDir, string stationId)
    {
        try
        {
            if (!Directory.Exists(blurbsDir))
                return;

            // Read fresh at sweep time (SPEC F44.2) — never a boot-frozen field — so a live edit to
            // Tts:BlurbRetentionHours changes the retention horizon on the very next blurb render.
            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(options.CurrentValue.BlurbRetentionHours);
            foreach (var entry in Directory.EnumerateFileSystemEntries(blurbsDir))
            {
                if (File.GetLastWriteTimeUtc(entry) < cutoff)
                    File.Delete(entry);
            }
        }
        catch (Exception ex)
        {
            // Deliberately broad: this GC step is opportunistic (SPEC F34.6) — a render that already
            // succeeded must return regardless of WHY the sweep couldn't finish (locked file, denied
            // permission, a race with a concurrent delete). The next blurb render retries.
            logger.LogWarning(ex, "Blurb retention sweep failed for station {StationId}; retrying on next blurb render", stationId);
        }
    }

    async Task<CuePoints?> MeasureCueAsync(string path, string hash, CancellationToken ct)
    {
        try
        {
            var result = await cueAnalyzer.AnalyzeAsync(path, ct);
            cueCache[hash] = result;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cue analysis failed for TTS clip {Hash}", hash);
            cueCache[hash] = null;
            return null;
        }
    }

    // pace (SPEC F98.2, PLAN T140) is the ACTUAL value, not a fingerprint of it — unlike the four
    // rule-set hashes above, there is no separate collection to canonicalize; the already-validated
    // double IS the term, formatted deterministically (invariant culture, so a comma-decimal host
    // locale can never split this into a different key than an equivalent dot-decimal one — same
    // discipline as KokoroPauseMarkup's own pause-tag formatting). Audio rendered at 0.85 is not
    // audio rendered at 1.0, and pace is invisible in copy.Text, so without this term a persona's
    // edited rate would keep serving its old cached clip forever, exactly the class of bug the four
    // rule-content hashes above already guard against for corrections/pronunciations.
    static string ComputeHash(
        string text, string voice, string stationId, string correctionsContentHash, string personaCorrectionsContentHash,
        string pronunciationsContentHash, string personaPronunciationsContentHash, string mergePolicyVersion, double pace) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(
                text + "|" + voice + "|" + stationId + "|" + correctionsContentHash + "|" + personaCorrectionsContentHash +
                "|" + pronunciationsContentHash + "|" + personaPronunciationsContentHash + "|" + mergePolicyVersion +
                "|" + pace.ToString("0.####", CultureInfo.InvariantCulture))));

    // Best-effort cleanup of a transient render file a failed File.Move left behind. IOException
    // AND UnauthorizedAccessException both swallowed — the TtsPreviewController.DeletePreviewArtifact
    // precedent (a locked file and a permission-denied delete are equally plausible causes a failed
    // Move can leave behind) — so a delete failure here never REPLACES the real Move exception the
    // caller is already rethrowing; this cleanup's own success was never the point.
    static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort; the Move failure the caller rethrows is the one that matters.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort; the Move failure the caller rethrows is the one that matters.
        }
    }
}
