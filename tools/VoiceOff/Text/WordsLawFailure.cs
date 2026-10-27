namespace GenWave.VoiceOff.Text;

/// <summary>One way a paragraph breaks the words law; <see cref="Tag"/> is set when an undeclared tag is the cause.</summary>
internal sealed record WordsLawFailure(int Paragraph, string? Tag, string Message);
