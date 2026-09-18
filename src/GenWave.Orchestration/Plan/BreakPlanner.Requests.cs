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

    // Verbatim copy of Orchestrator.BuildHandoffRequest (SPEC F92.2): built from the deferral's own
    // captured HandoffContext, never a fresh persona resolve. Takes the already-derived SegmentKind
    // rather than re-deriving it from SpeechDeferralKind — BuildHandoffDrainSlot computes it once
    // (T521 review) and passes it straight through.
    SegmentRequest BuildHandoffRequest(SegmentKind kind, HandoffContext handoff, StationIdentity identity) =>
        new(
            kind,
            handoff.Voice,
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
            CounterpartShowName: handoff.CounterpartShowName);

    // Verbatim copy of Orchestrator.BuildTimeDateRequest (SPEC F110.3/F141.2): LocalNow carries the
    // deferral's own armed Due, never a drain-time StationLocalNow() read.
    static SegmentRequest BuildTimeDateRequest(SpeechDeferral deferral, StationIdentity identity, TimeAnnouncementFreshness freshness) =>
        new(SegmentKind.TimeDate, identity.Voice, identity.Name, null, deferral.Due, identity.Id, PersonaName: null)
        {
            TimeDateFreshness = freshness,
        };

    // Verbatim copy of Orchestrator.BuildContextSegmentRequestAsync (SPEC F107.3/F107.6/F107.7),
    // split into a guard (ValidateContextContent, below) and this assembly step purely to stay
    // under the ~30-line method budget — every log line stays byte-identical to the original.
    async Task<(SegmentRequest Request, string ProviderKey)?> BuildContextSegmentRequestAsync(
        SpeechDeferral deferral, StationIdentity identity, DateTimeOffset drainNow, CancellationToken ct)
    {
        var providerKey = deferral.Discriminator ?? "(unknown)";
        if (ValidateContextContent(deferral, providerKey, drainNow) is not { } content)
            return null;

        var contextProviderSettings = contextSettings.For(providerKey);
        var (contextVoice, contextPersonaName) = await ResolveContextSegmentVoiceAsync(
            contextProviderSettings.PersonaId, providerKey, identity.Voice, ct);

        var contextReq = new SegmentRequest(
            SegmentKind.ContextSegment,
            contextVoice,
            identity.Name,
            null,
            StationLocalNow(),
            identity.Id,
            PersonaName: contextPersonaName,
            ContextFacts: content.SegmentFacts);

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
