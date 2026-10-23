namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Specs that change process environment variables run alone, so no other collection boots a host while a variable is set.</summary>
[CollectionDefinition("ProcessEnvironment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;
