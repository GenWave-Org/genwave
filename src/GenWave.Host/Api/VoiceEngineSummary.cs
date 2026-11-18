namespace GenWave.Host.Api;

/// <summary>One engine in <see cref="VoiceEnginesResponse"/>: its registry name and the capability profile it uses.</summary>
public sealed record VoiceEngineSummary(string Name, string Profile);
