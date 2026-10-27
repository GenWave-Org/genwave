using System.Globalization;
using GenWave.VoiceOff.Text;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>One engine's turn in a run: tier check, container gate, then every clip. The result is the engine's one summary line.</summary>
internal sealed class EngineRun(RenderServices services, RenderOptions options, GauntletSet set, RunLayout layout, TimingLog timing, RunProgress progress, TextWriter output)
{
    internal const string StoppedError = "engine stopped answering";

    internal async Task<string> RunAsync(EngineProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!AttemptsTier(profile))
        {
            // Already off the total: DropTierSkips ran before the first clip line, so N is right from line 1.
            return EngineSummary.ForNotAttempted(profile.Slug, $"tier {RunId.TierName(options.Tier)} not in its tiers");
        }

        ContainerGateResult container = await ContainerGate.EvaluateAsync(profile, services.Docker, services.Http, ct);
        if (container.Endpoint is not Uri endpoint || !container.Gate.Passed)
        {
            progress.Drop(RunProgress.ClipsFor(profile, set));
            return EngineSummary.ForFailedContainer(profile.Slug, container.Gate.Reason);
        }

        SpeechClient client = new(services.Http, endpoint, TimeSpan.FromSeconds(profile.TimeoutSeconds), services.Time);
        return await RenderClipsAsync(profile, client, ct);
    }

    /// <summary>Whether a run at <paramref name="tier"/> renders this engine; the one rule the real run and the dry run share.</summary>
    internal static bool AttemptsTier(EngineProfile profile, EngineTier tier)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Tiers.Contains(tier);
    }

    private bool AttemptsTier(EngineProfile profile) => AttemptsTier(profile, options.Tier);

    /// <summary>Takes the engines this run's tier skips off the total, so the first progress line already shows the final N.</summary>
    internal void DropTierSkips(IEnumerable<EngineProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        foreach (EngineProfile profile in profiles.Where(p => !AttemptsTier(p)))
        {
            progress.Drop(RunProgress.ClipsFor(profile, set));
        }
    }

    private async Task<string> RenderClipsAsync(EngineProfile profile, SpeechClient client, CancellationToken ct)
    {
        ClipRenderer renderer = new(client, profile, set, services.Match, layout);
        List<TimingRecord> records = [];
        DeadEngineBreaker breaker = new();
        try
        {
            await RenderLinesAsync(profile, renderer, records, breaker, ct);
        }
        finally
        {
            // Not cancellable: the clips already rendered must reach timing.jsonl even when the run is being stopped.
            foreach (TimingRecord record in records)
            {
                await timing.AppendAsync(record, CancellationToken.None);
            }
        }

        return EngineSummary.For(profile.Slug, records, breaker.Tripped);
    }

    private async Task RenderLinesAsync(EngineProfile profile, ClipRenderer renderer, List<TimingRecord> records, DeadEngineBreaker breaker, CancellationToken ct)
    {
        foreach (GauntletLine line in set.Lines)
        {
            ct.ThrowIfCancellationRequested();
            output.WriteLine(progress.NextLine());
            ClipOutcome outcome;
            bool rendered = !breaker.Tripped;
            if (rendered)
            {
                outcome = await renderer.RenderAsync(line, ct);
                breaker.Record(outcome);
            }
            else
            {
                outcome = ClipOutcome.Failed(StoppedError, 0);
            }

            progress.Finished(outcome, rendered);
            records.Add(new TimingRecord(profile.Slug, line.Id, options.Tier, outcome.WallMs, outcome.AudioMs, outcome.Ok, outcome.Error, outcome.Linear));
            output.WriteLine(ClipLine(profile, line, outcome));
        }
    }

    private static string ClipLine(EngineProfile profile, GauntletLine line, ClipOutcome outcome)
    {
        string head = $"{profile.Slug} {line.Id}";
        return outcome.Ok && outcome.AudioMs > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{head} rtf {outcome.WallMs / outcome.AudioMs:0.00}")
            : $"{head} ❌ {outcome.Error}";
    }
}
