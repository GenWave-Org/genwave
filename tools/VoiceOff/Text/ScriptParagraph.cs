namespace GenWave.VoiceOff.Text;

/// <summary>One numbered paragraph of the script; <see cref="Number"/> is 1-based.</summary>
internal sealed record ScriptParagraph(int Number, string Text);
