using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>One engine's row of <c>gates.json</c>. A gate that never ran is null; an engine not attempted says why in <paramref name="NotAttempted"/>.</summary>
internal sealed record GateEntry(
    string Slug,
    string License,
    bool Attempted,
    string? NotAttempted,
    GateResult? Container,
    GateResult? VoiceSelect);
