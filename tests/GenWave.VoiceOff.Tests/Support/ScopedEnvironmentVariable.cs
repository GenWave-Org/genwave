namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Sets a process environment variable and puts the old value back on dispose.</summary>
internal sealed class ScopedEnvironmentVariable : IDisposable
{
    private readonly string name;
    private readonly string? previous;

    internal ScopedEnvironmentVariable(string name, string value)
    {
        this.name = name;
        previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
}
