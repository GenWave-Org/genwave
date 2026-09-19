namespace GenWave.Orchestration;

using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

// The persona/voice resolution helpers EnqueuePatterAsync's in-scope steps call — split from
// BreakPlanner.cs purely for the ~300-line budget (csharp-best-practices).
public sealed partial class BreakPlanner
{
    DateTimeOffset StationLocalNow() => stationClock?.LocalNow ?? timeProvider.GetUtcNow();

    // Verbatim copy of Orchestrator.ResolvePersonaAsync (SPEC F35.3/F39.1): one personaAccessor read
    // per segment; never throws — any fault degrades to (stationVoice, null, null). PersonaId widened
    // for PLAN T527 (SPEC F188.4) — the id a caller resolves a SpeakerSnapshot with.
    async Task<(string Voice, string? PersonaName, long? PersonaId)> ResolvePersonaAsync(string stationVoice, CancellationToken ct)
    {
        try
        {
            var persona = await personaAccessor.ResolveAsync(ct);
            if (persona is not null)
                return (VoiceOf(persona, stationVoice), persona.Name, persona.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Falls through to (stationVoice, null, null) below — an accessor fault must never cost the slot.
        }

        return (stationVoice, null, null);
    }

    // PLAN T527 (SPEC F188.4/F189.6, round-2 review finding F1): resolves the SpeakerSnapshot a
    // slot's PersonaId names, memoized per plan via SpeakerResolution — null speakers (no
    // ISpeakerSnapshotSource wired) means no stamping at all, the ambient path stays byte-identical.
    // The planner-resolved `voice` is ALWAYS authoritative — it is the caller's own already-resolved
    // voice term (the persona ROW via ResolvePersonaAsync, the station's own identity.Voice, or
    // ResolveAnnouncementVoiceAsync's installed-voice check), never the card's. The snapshot is
    // aligned to it, not the reverse: a card whose voiceId is uninstalled, unknown, or unresolvable
    // (F79.4's blanked import, or any SpeakerSnapshotSource degrade to the station snapshot) must
    // never override the voice that already passed validation. Every caller then stamps its request's
    // Voice from this SAME `voice` parameter — never from the returned snapshot — so the two can
    // never disagree (ruling 9's invariant, satisfied by construction). Returning `snapshot with
    // { Voice = voice }` creates a new instance rather than the memoized one, which is fine — the
    // memo's own StationAsync/PersonaAsync call still only fires once per plan; the aligned copy's
    // stale ContentHash voice term is harmless too, since TtsRenderKey.ComputeSnapshotHash folds this
    // request's own (now-authoritative) Voice into the render key as its own separate term. Never
    // throws on its own: SpeakerResolution only wraps ISpeakerSnapshotSource, whose own contract
    // already degrades internally.
    static async Task<SpeakerSnapshot?> ResolveSpeakerAsync(
        SpeakerResolution? speakers, long? personaId, StationIdentity identity, string voice, CancellationToken ct)
    {
        if (speakers is null) return null;

        var snapshot = personaId is { } id
            ? await speakers.PersonaAsync(id, ct)
            : await speakers.StationAsync(identity, ct);

        return string.Equals(snapshot.Voice, voice, StringComparison.Ordinal) ? snapshot : snapshot with { Voice = voice };
    }

    // Verbatim copy of Orchestrator.ClaimAnnouncementsAsync (SPEC F144.1): a claim fault degrades to
    // an empty claim, never a faulted unit.
    async Task<IReadOnlyList<AnnouncementItem>> ClaimAnnouncementsAsync(IAnnouncementSource source, CancellationToken ct)
    {
        try
        {
            return await source.ClaimDeliverableAsync(AnnouncementVendCap, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Announcement claim failed — treating as an empty claim this unit; music is unaffected (SPEC F12.4).");
            return [];
        }
    }

    // Verbatim copy of Orchestrator.ResolveAnnouncementVoiceAsync (SPEC F144.2): a requested voice
    // the registry currently reports installed, else the station voice.
    async Task<string> ResolveAnnouncementVoiceAsync(string? requestedVoice, string stationVoice, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(requestedVoice) || voiceLister is null)
            return stationVoice;

        try
        {
            var known = await voiceLister.ListVoicesAsync(ct);
            return known.Contains(requestedVoice, StringComparer.Ordinal) ? requestedVoice : stationVoice;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Announcement voice registry unreachable — falling back to the station voice (SPEC F144.2)");
            return stationVoice;
        }
    }

    // Verbatim copy of Orchestrator.ResolveContextSegmentVoiceAsync (SPEC F107.7): an explicit
    // positive Context:{Key}:PersonaId resolves through ResolveContextPersonaAsync; zero/negative/
    // unresolvable degrades to the on-air DJ via ResolvePersonaAsync. PersonaId widened for PLAN
    // T527 (SPEC F188.4): an unresolvable explicit id names no persona (null), matching the fallback
    // it degrades to — the SpeakerSnapshot resolves the station in that case, exactly as the voice
    // already does.
    async Task<(string Voice, string? PersonaName, long? PersonaId)> ResolveContextSegmentVoiceAsync(
        long? configuredPersonaId, string providerKey, string stationVoice, CancellationToken ct)
    {
        if (configuredPersonaId is { } explicitPersonaId && explicitPersonaId > 0)
        {
            var resolved = await ResolveContextPersonaAsync(explicitPersonaId, providerKey, stationVoice, ct);
            if (resolved is { } r) return (r.Voice, r.Name, explicitPersonaId);

            return (stationVoice, null, null); // unresolvable explicit persona id — station voice, never a stall
        }

        return await ResolvePersonaAsync(stationVoice, ct);
    }

    // Verbatim copy of Orchestrator.ResolveContextPersonaAsync (SPEC F107.7): a missing row, no
    // personaStore wired, or any store fault all degrade to null.
    async Task<(string Voice, string Name)?> ResolveContextPersonaAsync(
        long personaId, string providerKey, string stationVoice, CancellationToken ct)
    {
        if (personaStore is null) return null;

        try
        {
            var persona = await personaStore.GetByIdAsync(personaId, ct);
            if (persona is null)
            {
                logger.LogWarning(
                    "Context provider {ProviderKey} names persona id={PersonaId} with no matching " +
                    "persona row — falling back to the station voice (SPEC F107.7 degrade).",
                    providerKey, personaId);
                return null;
            }

            return (VoiceOf(persona, stationVoice), persona.Name);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to resolve context provider {ProviderKey}'s persona id={PersonaId} — " +
                "falling back to the station voice (F12.4).",
                providerKey, personaId);
            return null;
        }
    }

    // Verbatim copy of Orchestrator.VoiceOf (SPEC F35.2/F92.2): "" is Persona's own "use the
    // station's default" sentinel, never "unset".
    static string VoiceOf(Persona persona, string stationVoice) =>
        string.IsNullOrEmpty(persona.Voice) ? stationVoice : persona.Voice;
}
