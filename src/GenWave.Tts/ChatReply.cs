namespace GenWave.Tts;

/// <summary>One chat completion's reply: the raw text and what the fixed clean-up (the parse) made of it.</summary>
internal sealed record ChatReply(string Raw, CrosstalkWriteResult Result);
