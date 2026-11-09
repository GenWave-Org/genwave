namespace GenWave.Host.Stats;

/// <summary>
/// A settled answer to "which compose project is this api in?" (gh-#629). <see cref="Name"/> is
/// null when the answer is a definitive "none" (not a compose deployment) — distinct from a
/// lookup that could not be answered at all, which the caller models as a null
/// <see cref="ComposeProjectLookup"/> and retries on the next poll.
/// </summary>
public sealed record ComposeProjectLookup(string? Name);
