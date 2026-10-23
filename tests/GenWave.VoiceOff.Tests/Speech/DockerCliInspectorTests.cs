using System.Runtime.Versioning;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Tests.Speech;

[UnsupportedOSPlatform("windows")]
public sealed class DockerCliInspectorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "voiceoff-docker-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AMissingDockerProgramMeansDockerDidNotAnswer() =>
        Assert.False((await new DockerCliInspector("genwave-no-such-program-t610").InspectAsync("engine-box", CancellationToken.None)).DockerAnswered);

    [Fact]
    public async Task ARunningContainerIsReadFromStdout() =>
        Assert.Equal(new ContainerState(true, true, "172.20.0.4"),
            await new DockerCliInspector(FakeDocker("echo 'true 172.20.0.4'; echo 'false 9.9.9.9' >&2; exit 0")).InspectAsync("engine-box", CancellationToken.None));

    [Fact]
    public async Task ANoSuchContainerFailureIsMissing() =>
        Assert.Equal(ContainerState.Missing("Error: No such container: engine-box"),
            await new DockerCliInspector(FakeDocker("echo 'junk'; echo 'Error: No such container: engine-box' >&2; exit 1")).InspectAsync("engine-box", CancellationToken.None));

    private string FakeDocker(string body)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "docker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
