namespace GenWave.Tts;

using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Production <see cref="ISpeakerSnapshotSource"/> (SPEC F189.2, gh-#772, PLAN T525): reads a
/// persona's card ONCE into one <see cref="SpeakerSnapshot"/> rather than the ambient
/// ActivePersona*Cache trio re-read at render time. Station snapshots always succeed;
/// persona snapshots degrade to the station snapshot (with one WARN naming the persona id) on
/// an unknown id or a card-store fault — this source never throws.
///
/// <para>
/// <b>Corrections split, by design (T526):</b> <see cref="SpeakerSnapshot.Corrections"/> here
/// carries the CARD's corrections only — never merged with the station's own <see
/// cref="SpeechCorrectionProvider.Current"/>. The station side is merged in at render time
/// instead, via <see cref="SpeechCorrectionProvider.BuildMerged"/>, the same seam
/// <c>TtsSegmentSource</c> already calls — this class only resolves the card half once, T526's
/// render branch performs the actual station∪card merge on every render (so a station corrections
/// edit still reaches an already-resolved snapshot without re-resolving the persona's card).
/// <see cref="SpeakerSnapshot.Rules"/>, in contrast, IS the full station∪card merge already
/// (<see cref="PronunciationRuleResolver.ResolveForRender"/>) — pronunciation rules have no
/// equivalent late-merge consumer, so resolving them once here is both correct and final.
/// </para>
/// </summary>
public sealed class SpeakerSnapshotSource(
    IPersonaCardByIdSource cards,
    IStationIdentityProvider identityProvider,
    PronunciationRuleProvider pronunciationRuleProvider,
    SpeechCorrectionProvider speechCorrectionProvider,
    ILogger<SpeakerSnapshotSource> logger) : ISpeakerSnapshotSource
{
    // Same literal sentinels ActivePersonaPronunciationRulesCache/ActivePersonaCorrectionsCache use
    // for "no card, or a card with no rules of this kind" — a station snapshot (card rules always
    // empty here) folds to these same two constants, exactly what the ambient path already
    // produces for a station-only deployment, so this seam's hash never disagrees with the ambient
    // one for the "no persona on air" case.
    const string NoCardPronunciationsHash = "no-card-pronunciations";
    const string NoCardCorrectionsHash = "no-card-corrections";

    public Task<SpeakerSnapshot> ForStationAsync(StationIdentity identity, CancellationToken ct) =>
        Task.FromResult(Build(null, null, identity.Voice, TtsPace.EngineDefault, [], []));

    public async Task<SpeakerSnapshot> ForPersonaAsync(long personaId, CancellationToken ct)
    {
        PersonaCard? card;
        try
        {
            card = await cards.GetCardAsync(personaId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Persona {PersonaId} card lookup failed; using the station snapshot", personaId);
            return await ForStationAsync(identityProvider.Current, ct);
        }

        if (card is null)
        {
            logger.LogWarning("Persona {PersonaId} has no card; using the station snapshot", personaId);
            return await ForStationAsync(identityProvider.Current, ct);
        }

        var cardRules = card.Pronunciations is { Count: > 0 }
            ? card.Pronunciations.Select(rule => new PronunciationRule(rule.Pattern, rule.Word, rule.Ipa)).ToList()
            : [];
        var cardCorrections = card.Corrections is { Count: > 0 }
            ? card.Corrections.Select(correction => new SpeechCorrection(correction.From, correction.To)).ToList()
            : [];

        var pace = TtsPace.Clamp(card.Voice is { } voice ? voice.Pace : TtsPace.EngineDefault);
        var voiceId = card.Voice is { VoiceId.Length: > 0 } spec ? spec.VoiceId : identityProvider.Current.Voice;

        return Build(personaId, card.Name, voiceId, pace, cardRules, cardCorrections);
    }

    SpeakerSnapshot Build(
        long? personaId,
        string? personaName,
        string voice,
        double pace,
        IReadOnlyList<PronunciationRule> cardRules,
        IReadOnlyList<SpeechCorrection> cardCorrections)
    {
        var rules = PronunciationRuleResolver.ResolveForRender(pronunciationRuleProvider.Current, cardRules);

        var cardPronunciationsHash = PronunciationRuleFingerprint.Compute(
            PronunciationRuleSet.Create(cardRules).Rules, NoCardPronunciationsHash);
        var cardCorrectionsHash = CorrectionsFingerprint.Compute(
            SpeechCorrectionSet.Create(cardCorrections).Rules, NoCardCorrectionsHash);

        var hash = SpeakerSnapshotFingerprint.Compute(
            voice,
            speechCorrectionProvider.ContentHash,
            cardCorrectionsHash,
            pronunciationRuleProvider.ContentHash,
            cardPronunciationsHash,
            TtsSegmentSource.MergePolicyVersion,
            pace);

        return new SpeakerSnapshot(personaId, personaName, voice, pace, rules, cardCorrections, hash);
    }
}
