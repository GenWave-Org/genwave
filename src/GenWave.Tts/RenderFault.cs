namespace GenWave.Tts;

/// <summary>What one finished speech render says about its engine (T768a): healthy, or failed with a plain reason.</summary>
sealed record RenderFault(bool IsFailure, string Reason)
{
    public static RenderFault Success { get; } = new(false, string.Empty);

    public static RenderFault Failure(string reason) => new(true, reason);
}
