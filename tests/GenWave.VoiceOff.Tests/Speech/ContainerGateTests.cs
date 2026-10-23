using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Speech;

public sealed class ContainerGateTests
{
    private static EngineProfile Profile(string? endpoint = null) =>
        EngineProfileReader.Parse(
            EngineProfileJson.With(n => { if (endpoint is not null) { n["endpoint"] = endpoint; } }), "alpha.json");

    private static async Task<ContainerGateResult> Gate(EngineProfile profile, ContainerState state, FakeDockerInspector? docker = null)
    {
        using HttpClient http = SpeechClient.CreateHttpClient();
        return await ContainerGate.EvaluateAsync(profile, docker ?? new FakeDockerInspector(state), http, CancellationToken.None);
    }

    [Fact]
    public async Task AMissingContainerFails()
    {
        ContainerGateResult result = await Gate(Profile(), ContainerState.Missing("No such object"));
        Assert.Contains("not found", result.Gate.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADockerThatCouldNotAnswerIsNotReportedAsAMissingContainer()
    {
        ContainerGateResult result = await Gate(Profile(), ContainerState.DockerUnavailable("Cannot connect to the Docker daemon"));
        Assert.Equal("docker could not inspect alpha-box: Cannot connect to the Docker daemon", result.Gate.Reason);
    }

    [Fact]
    public async Task AStoppedContainerFails()
    {
        ContainerGateResult result = await Gate(Profile(), new ContainerState(true, false, "10.0.0.2"));
        Assert.Equal((false, "container alpha-box is not running"), (result.Gate.Passed, result.Gate.Reason));
    }

    [Fact]
    public async Task ARunningContainerWithoutAnAddressFails()
    {
        ContainerGateResult result = await Gate(Profile(), new ContainerState(true, true, null));
        Assert.Contains("no network address", result.Gate.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARunningContainerWhoseVoicesAnswerPasses()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync();
        ContainerGateResult result = await Gate(Profile(stub.BaseUri.AbsoluteUri), new ContainerState(true, true, "10.0.0.2"));
        Assert.True(result.Gate.Passed);
    }

    [Fact]
    public async Task ThePassingGateReportsTheEndpointItUsed()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync();
        ContainerGateResult result = await Gate(Profile(stub.BaseUri.AbsoluteUri), new ContainerState(true, true, "10.0.0.2"));
        Assert.Equal(stub.BaseUri, result.Endpoint);
    }

    [Fact]
    public async Task AVoicesEndpointThatFailsFails()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(voicesJson: null);
        ContainerGateResult result = await Gate(Profile(stub.BaseUri.AbsoluteUri), new ContainerState(true, true, "10.0.0.2"));
        Assert.Contains("voices endpoint", result.Gate.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheInspectorIsAskedForTheProfilesContainer()
    {
        ContainerState state = ContainerState.Missing("x");
        FakeDockerInspector docker = new(state);
        await Gate(Profile(), state, docker);
        Assert.Equal(["alpha-box"], docker.Asked);
    }

    [Fact]
    public void WithoutAnOverrideTheEndpointIsTheContainerAddressOnTheProfilePort() =>
        Assert.Equal("http://10.0.0.2:8880/", ContainerGate.Resolve(Profile(), new ContainerState(true, true, "10.0.0.2"))?.AbsoluteUri);

    [Fact]
    public void ParseReadsRunningAndTheFirstAddress() =>
        Assert.Equal(new ContainerState(true, true, "172.20.0.4"), DockerCliInspector.Parse("true 172.20.0.4 \n"));

    [Fact]
    public void ParseReadsAStoppedContainerWithNoAddress() =>
        Assert.Equal(new ContainerState(true, false, null), DockerCliInspector.Parse("false \n"));

    [Fact]
    public void TheArgvIsExactlyAReadOnlyInspectWithTheNameAfterTheDoubleDash() =>
        Assert.Equal(
            ["inspect", "--type", "container", "--format", "{{.State.Running}} {{range .NetworkSettings.Networks}}{{.IPAddress}} {{end}}", "--", "-rm"],
            DockerCliInspector.Arguments("-rm"));

    [Fact]
    public void NoSuchContainerIsAMissingContainer() =>
        Assert.Equal(
            (false, true),
            (DockerCliInspector.Classify("Error response from daemon: No such container: x").Exists,
             DockerCliInspector.Classify("Error response from daemon: No such container: x").DockerAnswered));

    [Fact]
    public void ADaemonThatIsDownIsDockerUnavailable() =>
        Assert.False(DockerCliInspector.Classify("Cannot connect to the Docker daemon at unix:///var/run/docker.sock. Is the docker daemon running?").DockerAnswered);

    [Fact]
    public void ADaemonSocketThatIsMissingIsNotAMissingContainer() =>
        Assert.False(DockerCliInspector.Classify(
            "failed to connect to the docker API at unix:///nonexistent/docker.sock; check if the path is correct and if the daemon is listening on this socket: dial unix /nonexistent/docker.sock: connect: no such file or directory").DockerAnswered);
}
