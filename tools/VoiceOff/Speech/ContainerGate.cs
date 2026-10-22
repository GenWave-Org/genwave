namespace GenWave.VoiceOff.Speech;

/// <summary>
/// The container gate (SPEC F214.6): <c>docker inspect</c> says the container is running and its voices
/// endpoint answers with at least one voice. The tool never starts or stops anything.
/// </summary>
internal static class ContainerGate
{
    internal static async Task<ContainerGateResult> EvaluateAsync(
        EngineProfile profile, IDockerInspector docker, HttpClient http, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(http);

        ContainerState state = await docker.InspectAsync(profile.Container, ct);
        if (!state.DockerAnswered)
        {
            return Failed($"docker could not inspect {profile.Container}: {state.Detail}");
        }

        if (!state.Exists)
        {
            return Failed($"container {profile.Container} was not found: {state.Detail}");
        }

        if (!state.Running)
        {
            return Failed($"container {profile.Container} is not running");
        }

        Uri? endpoint = Resolve(profile, state);
        if (endpoint is null)
        {
            return Failed($"container {profile.Container} has no network address");
        }

        VoiceListResult voices = await new SpeechClient(http, endpoint, TimeSpan.FromSeconds(profile.TimeoutSeconds)).ListVoicesAsync(ct);
        return voices.Ok
            ? new ContainerGateResult(GateResult.Pass($"{voices.VoiceIds.Count} voices at {endpoint}"), endpoint)
            : Failed($"voices endpoint at {endpoint} failed: {voices.Error?.Message}");
    }

    /// <summary>The profile's own endpoint when it names one, else the running container's address on the profile's port.</summary>
    internal static Uri? Resolve(EngineProfile profile, ContainerState state) =>
        profile.Endpoint
        ?? (state.IpAddress is { Length: > 0 } ip && Uri.TryCreate($"http://{ip}:{profile.Port}", UriKind.Absolute, out Uri? uri)
            ? uri
            : null);

    private static ContainerGateResult Failed(string reason) => new(GateResult.Fail(reason), null);
}
