namespace GenWave.Tts.Tests.Fakes;

/// <summary>One captured prompt pair (STORY-567): the system and user messages the LLM client was handed.</summary>
sealed record GoldenPrompt(string Name, string System, string User);
