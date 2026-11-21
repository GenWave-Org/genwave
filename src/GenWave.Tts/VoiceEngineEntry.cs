namespace GenWave.Tts;

/// <summary>One <c>Tts:Engines:&lt;name&gt;</c> config entry (SPEC F219.1).</summary>
public sealed class VoiceEngineEntry
{
    public string Endpoint { get; set; } = "";

    public string Profile { get; set; } = "";

    /// <summary>This engine's own render budget (F219.6); default 30. Seeded kokoro and piper entries copy <c>Tts:RenderBudgetSeconds</c>.</summary>
    public int RenderBudgetSeconds { get; set; } = 30;
}
