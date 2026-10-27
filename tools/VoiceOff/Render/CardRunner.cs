using System.ComponentModel;
using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>
/// Renders the planned cards in order (SPEC F217.5, F217.10): a refused card, a failed container gate, a stopped engine or a failed
/// paragraph is a <c>rendered:false</c> row with its reason, never a failed run. <c>run.json</c> is rewritten after every card.
/// </summary>
internal sealed class CardRunner(RenderServices services, EngineProfileSet engines, RunLayout layout, RenderLog log, RunProgress progress, TextWriter output)
{
    internal const string StoppedReason = "engine stopped answering";

    /// <summary>What <c>run.json</c> says of a card from the moment its old files are gone until its real outcome is known.</summary>
    internal const string UnfinishedReason = "render did not finish";

    private readonly Dictionary<string, EngineSession> sessions = new(StringComparer.Ordinal);

    internal async Task<RunManifest> RunAsync(IReadOnlyList<CardPlan> plans, RunManifest manifest, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(manifest);
        foreach (CardPlan plan in plans)
        {
            ct.ThrowIfCancellationRequested();
            output.WriteLine($"{plan.Card.Id}: {plan.Card.Engine} on {CardHostName.Of(plan.Card.Host)}");
            CardEntry unfinished = new(CardHostName.Of(plan.Card.Host), false, UnfinishedReason);
            CardOutcome outcome;
            try
            {
                await ResetAsync(plan.Card.Id, ct);
                // The old clip is gone: until the real outcome lands, run.json must not call the card rendered.
                manifest = await RecordAsync(manifest, plan.Card.Id, unfinished);
                outcome = await OutcomeAsync(plan, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                // A disk or process fault on this card is this card's row, not a stack trace and not a dropped card.
                outcome = CardOutcome.Failed($"render stopped: {ex.Message}");
            }

            output.WriteLine(outcome.Rendered ? $"{plan.Card.Id} ✅ rendered" : $"{plan.Card.Id} ❌ {outcome.Reason}");
            manifest = await RecordAsync(manifest, plan.Card.Id, unfinished with { Rendered = outcome.Rendered, Reason = outcome.Reason });
        }

        return manifest;
    }

    private async Task<RunManifest> RecordAsync(RunManifest manifest, string cardId, CardEntry entry)
    {
        RunManifest next = manifest.With(cardId, entry);
        await next.WriteAsync(layout.RunJsonPath, CancellationToken.None);
        return next;
    }

    // A card is rewritten whole: nothing of an earlier render may survive next to the new one.
    private async Task ResetAsync(string cardId, CancellationToken ct)
    {
        // The log goes first: a corrupt one stops the card before any of its files are touched.
        await log.RemoveCardAsync(cardId, ct);
        if (Directory.Exists(layout.RawDirectory(cardId)))
        {
            Directory.Delete(layout.RawDirectory(cardId), recursive: true);
        }

        string clip = layout.ClipPath(cardId);
        if (File.Exists(clip))
        {
            File.Delete(clip);
        }
    }

    private async Task<CardOutcome> OutcomeAsync(CardPlan plan, CancellationToken ct)
    {
        if (plan.Refusal is { } refusal)
        {
            return CardOutcome.Failed(refusal);
        }

        EngineSession session = await SessionAsync(plan.Card.Engine, ct);
        if (session.Renderer is not { } renderer)
        {
            progress.Drop(plan.Paragraphs.Count);
            return CardOutcome.Failed($"container: {session.GateFailure}");
        }

        if (session.Breaker.Tripped)
        {
            progress.Drop(plan.Paragraphs.Count);
            return CardOutcome.Failed(StoppedReason);
        }

        CardOutcome outcome = await renderer.RenderAsync(plan, ct);
        session.Breaker.Record(outcome);
        return outcome;
    }

    private async Task<EngineSession> SessionAsync(string slug, CancellationToken ct)
    {
        if (sessions.TryGetValue(slug, out EngineSession? known))
        {
            return known;
        }

        EngineProfile profile = engines.Profiles.First(p => string.Equals(p.Slug, slug, StringComparison.Ordinal));
        ContainerGateResult gate = await ContainerGate.EvaluateAsync(profile, services.Docker, services.Http, ct);
        EngineSession session = gate.Endpoint is Uri endpoint && gate.Gate.Passed
            ? new EngineSession(
                new CardRenderer(
                    new SpeechClient(services.Http, endpoint, TimeSpan.FromSeconds(profile.TimeoutSeconds), services.Time),
                    services.Match, layout, log, progress, output),
                null,
                new DeadEngineBreaker())
            : EngineSession.Unavailable(gate.Gate.Reason);
        sessions[slug] = session;
        return session;
    }
}
