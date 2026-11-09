namespace GenWave.Tts;

/// <summary>The two messages one break's chat-completion request carries (SPEC F218.27).</summary>
internal sealed record BreakPrompt(string System, string User);
