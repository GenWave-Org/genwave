namespace GenWave.Host.Api;

/// <summary>
/// One engine in <see cref="VoiceEnginesResponse"/>: its registry name, the capability profile it uses, the render
/// time limit in force for it right now (a <c>Tts:EngineRenderBudgets</c> override, else its own config value), and
/// that own config value on its own (what the engine falls back to when the override is cleared).
/// </summary>
public sealed record VoiceEngineSummary(string Name, string Profile, int RenderBudgetSeconds, int ConfigRenderBudgetSeconds);
