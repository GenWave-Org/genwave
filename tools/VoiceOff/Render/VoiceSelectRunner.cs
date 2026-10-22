using GenWave.VoiceOff.Audio;
using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>
/// Runs the voiceSelect gate for one engine (SPEC F214.6): one fixed line (the first station-id line, plain lane) rendered with
/// one voice twice and another once into <c>gates/&lt;slug&gt;/</c>, matched, fingerprinted and compared. These renders are not clips and
/// leave no timing record. A render or match problem is a failed gate with the reason, never an exception.
/// </summary>
internal sealed class VoiceSelectRunner(SpeechClient client, EngineProfile profile, GauntletSet set, LoudnessMatch match, RunLayout layout)
{
    internal async Task<GateResult> RunAsync(CancellationToken ct)
    {
        (GauntletRegister A, GauntletRegister B)? registers = VoiceSelectGate.PickRegisters(profile);
        if (registers is null)
        {
            return VoiceSelectGate.OneVoice();
        }

        GauntletLine? line = set.Lines.FirstOrDefault(l => l is { Class: GauntletClass.StationId, Text: not null });
        if (line?.Text is not string lineText)
        {
            return GateResult.Fail("the gauntlet has no station-id line to compare voices with");
        }

        string text = ClipTextBuilder.Build(lineText, RenderLane.Plain, profile, set);
        string voiceA = profile.VoiceFor(registers.Value.A);
        string voiceB = profile.VoiceFor(registers.Value.B);
        Directory.CreateDirectory(Path.GetDirectoryName(layout.GatePath(profile.Slug, "a1")) ?? layout.RunDirectory);

        string? problem = await RenderMatchedAsync(text, voiceA, "a1", ct)
            ?? await RenderMatchedAsync(text, voiceA, "a2", ct)
            ?? await RenderMatchedAsync(text, voiceB, "b", ct);
        if (problem is not null)
        {
            return GateResult.Fail($"voiceSelect could not render: {problem}");
        }

        try
        {
            return VoiceSelectGate.Evaluate(
                await AudioFingerprint.FromFileAsync(layout.GatePath(profile.Slug, "a1"), ct),
                await AudioFingerprint.FromFileAsync(layout.GatePath(profile.Slug, "a2"), ct),
                await AudioFingerprint.FromFileAsync(layout.GatePath(profile.Slug, "b"), ct));
        }
        catch (InvalidOperationException ex)
        {
            return GateResult.Fail($"voiceSelect could not compare the renders: {ex.Message}");
        }
    }

    // Null when the render was made and matched; otherwise why not.
    private async Task<string?> RenderMatchedAsync(string text, string voice, string name, CancellationToken ct)
    {
        SpeechResult result = await client.RenderAsync(text, voice, profile.Speed, ct);
        if (result.Error is { } error)
        {
            return error.Message;
        }

        string raw = layout.GatePath(profile.Slug, name, raw: true);
        await File.WriteAllBytesAsync(raw, result.Audio.ToArray(), ct);
        LoudnessMatchResult matched = await match.MatchAsync(raw, layout.GatePath(profile.Slug, name), ct);
        return matched.Ok ? null : matched.Error ?? "loudness match failed";
    }
}
