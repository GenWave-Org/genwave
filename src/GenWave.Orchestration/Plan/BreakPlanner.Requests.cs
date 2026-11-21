namespace GenWave.Orchestration;

using Microsoft.Extensions.Logging;
using GenWave.Core.Domain;

// The SegmentRequest builders + the drain's onExpired callback — split from BreakPlanner.cs purely
// for the ~300-line budget (csharp-best-practices).
public sealed partial class BreakPlanner
{
    // Verbatim copy of Orchestrator.BuildStationIdRequest (SPEC F110.2/F117.2): the one StationId
    // request shape, station-voiced, no persona resolve.
    SegmentRequest BuildStationIdRequest(StationIdentity identity) =>
        new(SegmentKind.StationId, identity.Voice, identity.Name, null, StationLocalNow(), identity.Id, PersonaName: null);

    // Verbatim copy of Orchestrator.BuildPooledStationIdItem (SPEC F110.2): an authored station-id
    // row airs verbatim, stamped with the SegmentKind its own mapping never sets.
    static MediaItem BuildPooledStationIdItem(MediaReference pooled) =>
        pooled.ToMediaItem() with { SegmentKind = SegmentKind.StationId };

    // Built from the deferral's own captured HandoffContext (SPEC F92.2). Takes the already-derived
    // SegmentKind rather than re-deriving it — BuildHandoffDrainSlotAsync computes it once. The
    // HandoffContext is captured at ARM time, up to a ceremony-window ahead of the drain, so its Voice
    // (the persona ROW's) and Speaker (the card as it was then) can be stale against a Tts:Engine
    // switch made since: when a snapshot source is wired and the half names a persona, the voice is
    // re-resolved HERE for the active engine through ResolvePersonaSpeakerAsync (SPEC F219.8/F219.11,
    // "resolved per render"). Without one the arm-time pair is used as before. Either way the request's
    // Voice and the snapshot's Voice are the same string (round-2 finding F3 / ruling 9): the
    // snapshot is aligned to the voice, never the reverse. The ARMED snapshot is kept (SPEC F189.5: its
    // rules, pace and content hash were captured with the counterpart); only its Voice and VoiceMissing
    // are replaced by the fresh read. A card-less half keeps handoff.Voice.
    async Task<SegmentRequest> BuildHandoffRequestAsync(
        SegmentKind kind, HandoffContext handoff, StationIdentity identity, SpeakerResolution? speakers, CancellationToken ct)
    {
        var voice = handoff.Voice;
        var speaker = handoff.Speaker is { } armed ? armed with { Voice = voice } : null;
        if (speakers is not null && handoff.PersonaId is not null)
        {
            var (fresh, freshVoice) = await ResolvePersonaSpeakerAsync(speakers, handoff.PersonaId, identity, handoff.Voice, ct);
            voice = freshVoice;

            // SPEC F189.5: the armed snapshot (rules, pace, content hash) rides through; only the voice and
            // its missing-ness are re-read for the engine active now.
            speaker = handoff.Speaker is { } armedSnapshot
                ? armedSnapshot with { Voice = freshVoice, VoiceMissing = fresh?.VoiceMissing ?? false }
                : fresh;
        }

        return StampShift(new(
            kind,
            voice,
            identity.Name,
            null,
            StationLocalNow(),
            identity.Id,
            handoff.PersonaName,
            handoff.CounterpartName,
            CrossingTrackTitle: handoff.CrossingTrackTitle,
            CrossingTrackArtist: handoff.CrossingTrackArtist,
            ShowName: handoff.ShowName,
            ShowFlavor: handoff.ShowFlavor,
            CounterpartShowName: handoff.CounterpartShowName)
        {
            Speaker = speaker,
            Handoff = HandoffBetween(kind, handoff),
        }, handoff.PersonaId);
    }

    // SPEC F218.15(b): both sides known and distinct, else no handoff to remember (a same-persona show
    // transition has no counterpart; a music-only half never resolved one).
    static HandoffParticipants? HandoffBetween(SegmentKind kind, HandoffContext handoff)
    {
        if (handoff is not { PersonaId: { } self, PersonaName: { } selfName, CounterpartPersonaId: { } other, CounterpartName: { } otherName, BoundaryAt: { } boundaryAt }
            || self == other)
            return null;

        var me = new CrosstalkParticipant(self, selfName);
        var them = new CrosstalkParticipant(other, otherName);
        return kind switch
        {
            SegmentKind.SignOff => new HandoffParticipants(me, them, boundaryAt),
            SegmentKind.SignOn => new HandoffParticipants(them, me, boundaryAt),
            _ => null,
        };
    }

    // Verbatim copy of Orchestrator.BuildTimeDateRequest (SPEC F110.3/F141.2): LocalNow carries the
    // deferral's own armed Due, never a drain-time StationLocalNow() read. Speaker (PLAN T527, SPEC
    // F188.4) is always the STATION's snapshot — a TimeDate announcement never speaks in a persona's
    // own voice — resolved by the caller (BuildTimeDateDrainSlotAsync), already aligned to
    // identity.Voice there (round-2 review finding F1), so this stays a pure builder.
    static SegmentRequest BuildTimeDateRequest(
        SpeechDeferral deferral, StationIdentity identity, TimeAnnouncementFreshness freshness, SpeakerSnapshot? speaker) =>
        new(SegmentKind.TimeDate, identity.Voice, identity.Name, null, deferral.Due, identity.Id, PersonaName: null)
        {
            TimeDateFreshness = freshness,
            Speaker = speaker,
        };

    // Verbatim copy of Orchestrator.BuildContextSegmentRequestAsync (SPEC F107.3/F107.6/F107.7),
    // split into a guard (ValidateContextContent, below) and this assembly step purely to stay
    // under the ~30-line method budget — every log line stays byte-identical to the original.
    // ContextProviderKey is the deferral's own Discriminator (null when absent): "(unknown)" is a log
    // label only, so a keyless segment is never remembered as "read the (unknown)" (PLAN T702c).
    // Speaker (PLAN T527, SPEC F188.4/F189.4): resolved off the SAME PersonaId the voice/name tuple
    // just named, and — per round-2 review finding F1 — the request's stamped Voice is this method's
    // own already-resolved contextVoice term; the snapshot aligns to it, never the reverse, so the
    // two can never disagree at render time.
    async Task<(SegmentRequest Request, string ProviderKey)?> BuildContextSegmentRequestAsync(
        SpeechDeferral deferral, StationIdentity identity, DateTimeOffset drainNow, SpeakerResolution? speakers, CancellationToken ct)
    {
        var providerKey = deferral.Discriminator ?? "(unknown)";
        if (ValidateContextContent(deferral, providerKey, drainNow) is not { } content)
            return null;

        var contextProviderSettings = contextSettings.For(providerKey);
        var (contextVoice, contextPersonaName, contextPersonaId) = await ResolveContextSegmentVoiceAsync(
            contextProviderSettings.PersonaId, providerKey, identity.Voice, ct);

        var (speaker, resolvedVoice) = await ResolvePersonaSpeakerAsync(speakers, contextPersonaId, identity, contextVoice, ct);

        var contextReq = StampShift(new SegmentRequest(
            SegmentKind.ContextSegment,
            resolvedVoice,
            identity.Name,
            null,
            StationLocalNow(),
            identity.Id,
            PersonaName: contextPersonaName,
            ContextFacts: content.SegmentFacts)
        {
            Speaker = speaker,
            ContextProviderKey = deferral.Discriminator,
        }, contextPersonaId);

        return (contextReq, providerKey);
    }

    // SPEC F107.3/F107.6 — re-verify freshness HERE, at drain time: the payload was captured at
    // enqueue time, and the boundary this drain fires at can land well after that. Each failure
    // logged at Information, naming the provider, never the facts themselves (SPEC F108.3).
    ContextSegmentFacts? ValidateContextContent(SpeechDeferral deferral, string providerKey, DateTimeOffset drainNow)
    {
        if (deferral.Context is not { } content)
        {
            logger.LogInformation(
                "Context segment for provider {ProviderKey} skipped at drain time: no " +
                "content captured (SPEC F107.6).", providerKey);
            return null;
        }

        if (content.FreshUntil <= drainNow)
        {
            logger.LogInformation(
                "Context segment for provider {ProviderKey} skipped at drain time: stale " +
                "(past FreshUntil) — music continues (SPEC F107.6).", providerKey);
            return null;
        }

        if (string.IsNullOrWhiteSpace(content.SegmentFacts))
        {
            logger.LogInformation(
                "Context segment for provider {ProviderKey} skipped at drain time: no " +
                "segment facts (SPEC F107.6).", providerKey);
            return null;
        }

        return content;
    }

    // Verbatim copy of Orchestrator.LogTimeDateExpiry — TryDequeueDue's own onExpired callback.
    void LogTimeDateExpiry(SpeechDeferral deferral, TimeSpan lateness, TimeSpan budget) =>
        logger.LogWarning(
            "TimeDate deferral armed for {ArmedHour:HH:mm} dropped undrained — {LatenessSeconds:F0}s " +
            "past its armed hour (budget {BudgetSeconds:F0}s); a late time check would invent the hour.",
            deferral.Due, lateness.TotalSeconds, budget.TotalSeconds);
}
