namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/gardener/file-actions/dry-run</c>'s own request body (SPEC F154.1, F154.3; STORY-379;
/// PLAN T381, gh-#529). <see cref="Verb"/> is the wire verb token (<c>retag</c>/<c>rename</c>/<c>move</c>,
/// parsed via <see cref="GenWave.Core.Domain.FileActionVerbTokens.TryParse"/>) — declared
/// <see langword="string"/>? rather than the enum itself so an unknown or absent value is a plain 400,
/// never a model-binding failure. <see cref="Target"/> is the optional rename name / move destination
/// directory (<see cref="GenWave.Core.Abstractions.IFileActionPlanner.Plan"/>'s own remarks on what it
/// means per verb).
/// </summary>
public sealed record FileActionDryRunRequest(long MediaId, string? Verb, string? Target);
