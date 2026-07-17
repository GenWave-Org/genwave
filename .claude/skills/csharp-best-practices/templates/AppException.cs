namespace GenWave.Example.Domain;

/// <summary>
/// Base for domain exceptions: bugs and environment failures that a
/// developer or operator must act on (expected/recoverable failures use
/// Result&lt;T&gt; instead). Subclasses are specific and carry the context
/// needed to debug without re-running: IDs, paths, states.
///
/// Adapt: rename per project (e.g. GenWaveException) and add one sealed
/// subclass per failure family — each in its own file (house rule).
/// </summary>
public abstract class AppException : Exception
{
    /// <summary>Stable machine-readable code for logs/alerts, e.g. "track.missing".</summary>
    public string Code { get; }

    protected AppException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public override string ToString() => $"[{Code}] {base.ToString()}";
}

/* Example subclass — copy to its own file:

public sealed class TrackNotFoundException : AppException
{
    public int TrackId { get; }

    public TrackNotFoundException(int trackId)
        : base("track.not-found", $"Track {trackId} does not exist in the catalog.")
    {
        TrackId = trackId;
    }
}
*/
