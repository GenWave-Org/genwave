namespace GenWave.PatterBench;

/// <summary>The endpoint refused, timed out or answered with an error, so the run cannot be trusted.</summary>
internal sealed class EndpointUnreachableException(string endpoint, string reason, Exception inner)
    : Exception($"cannot reach {endpoint}: {reason}", inner);
