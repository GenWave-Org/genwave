using System.Globalization;
using GenWave.VoiceOff.Audio;
using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Render;

/// <summary>One engine's turn in a run: tier check, container gate, voiceSelect gate, then every clip while its container is sampled.</summary>
internal sealed class EngineRun(RenderServices services, RenderOptions options, GauntletSet set, RunLayout layout, TimingLog timing, RunProgress progress, TextWriter output)
{
    internal const string StoppedError = "engine stopped answering";

    internal async Task<EngineRunResult> RunAsync(EngineProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!AttemptsTier(profile))
        {
            // Already off the total: DropTierSkips ran before the first clip line, so N is right from line 1.
            return Skipped(profile, $"tier {RunId.TierName(options.Tier)} not in its tiers");
        }

        ContainerGateResult container = await ContainerGate.EvaluateAsync(profile, services.Docker, services.Http, ct);
        if (container.Endpoint is not Uri endpoint || !container.Gate.Passed)
        {
            progress.Drop(RunProgress.ClipsFor(profile, set));
            GateEntry failed = Entry(profile, container.Gate, null);
            return new EngineRunResult(failed, [], new ResourceUsage(null, null), false);
        }

        SpeechClient client = new(services.Http, endpoint, TimeSpan.FromSeconds(profile.TimeoutSeconds), services.Time);
        GateResult voiceSelect = await new VoiceSelectRunner(client, profile, set, services.Match, layout).RunAsync(ct);
        if (!voiceSelect.Passed)
        {
            output.WriteLine($"{profile.Slug}: voiceSelect {Mark(false)} {voiceSelect.Reason}");
        }

        GateEntry gates = Entry(profile, container.Gate, voiceSelect);
        return await RenderClipsAsync(profile, client, gates, ct);
    }

    private bool AttemptsTier(EngineProfile profile) => profile.Tiers.Contains(options.Tier);

    /// <summary>Takes the engines this run's tier skips off the total, so the first progress line already shows the final N.</summary>
    internal void DropTierSkips(IEnumerable<EngineProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        foreach (EngineProfile profile in profiles.Where(p => !AttemptsTier(p)))
        {
            progress.Drop(RunProgress.ClipsFor(profile, set));
        }
    }

    private static EngineRunResult Skipped(EngineProfile profile, string reason) =>
        new(new GateEntry(profile.Slug, profile.License, false, reason, null, null), [], new ResourceUsage(null, null), false);

    private static GateEntry Entry(EngineProfile profile, GateResult container, GateResult? voiceSelect) =>
        new(profile.Slug, profile.License, true, null, container, voiceSelect);

    private static string Mark(bool passed) => passed ? "✅" : "❌";

    // Records are held until the loop ends: the footprint peaks are only known then, and each of the engine's records carries them.
    // They are appended in the finally, so a Ctrl-C keeps the clips rendered so far.
    private async Task<EngineRunResult> RenderClipsAsync(EngineProfile profile, SpeechClient client, GateEntry gates, CancellationToken ct)
    {
        ClipRenderer renderer = new(client, profile, set, services.Match, layout);
        ResourceSampler sampler = new(services.Probe, profile.Container, services.Time, options.Tier == EngineTier.Gpu);
        using CancellationTokenSource window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task sampling = sampler.RunAsync(window.Token);
        List<TimingRecord> records = [];
        DeadEngineBreaker breaker = new();
        try
        {
            foreach (GauntletLine line in set.Lines)
            {
                foreach (RenderLane lane in LanePlan.For(profile))
                {
                    ct.ThrowIfCancellationRequested();
                    output.WriteLine(progress.NextLine());
                    ClipOutcome outcome;
                    bool rendered = !breaker.Tripped;
                    if (rendered)
                    {
                        outcome = await renderer.RenderAsync(line, lane, ct);
                        breaker.Record(outcome);
                    }
                    else
                    {
                        outcome = ClipOutcome.Failed(StoppedError, 0);
                    }

                    progress.Finished(outcome, rendered);

                    records.Add(Record(profile, line, lane, outcome));
                    output.WriteLine(ClipLine(profile, line, lane, outcome));
                }
            }
        }
        finally
        {
            await window.CancelAsync();
            await sampling;
            await AppendAsync(records, sampler.Usage);
        }

        ResourceUsage usage = sampler.Usage;
        return new EngineRunResult(gates, WithUsage(records, usage), usage, breaker.Tripped);
    }

    private static List<TimingRecord> WithUsage(List<TimingRecord> records, ResourceUsage usage) =>
        records.Select(r => r with { RssMb = usage.PeakRssMb, VramMb = usage.PeakVramMb }).ToList();

    // Not cancellable: the clips already rendered must reach timing.jsonl even when the run is being stopped.
    private async Task AppendAsync(List<TimingRecord> records, ResourceUsage usage)
    {
        foreach (TimingRecord record in WithUsage(records, usage))
        {
            await timing.AppendAsync(record, CancellationToken.None);
        }
    }

    private TimingRecord Record(EngineProfile profile, GauntletLine line, RenderLane lane, ClipOutcome outcome) =>
        new(profile.Slug, lane, line.Id, options.Tier, outcome.WallMs, outcome.AudioMs, null, null, outcome.Ok, outcome.Error, outcome.Linear);

    private static string ClipLine(EngineProfile profile, GauntletLine line, RenderLane lane, ClipOutcome outcome)
    {
        string head = $"{profile.Slug} {lane.ToString().ToLowerInvariant()} {line.Id}";
        return outcome.Ok && outcome.AudioMs > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{head} rtf {outcome.WallMs / outcome.AudioMs:0.00}")
            : $"{head} ❌ {outcome.Error}";
    }
}
