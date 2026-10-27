namespace GenWave.VoiceOff.Render;

/// <summary>One line of <c>renders.jsonl</c> (F217.5): how one paragraph of one card went. <see cref="Chars"/> is the length of the text the engine was sent.</summary>
internal sealed record RenderRecord(string Card, int N, int Chars, double WallMs, double AudioMs, bool Ok, string? Error);
