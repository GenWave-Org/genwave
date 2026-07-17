namespace GenWave.Host.Tests;

/// <summary>
/// xUnit collection for specs whose <c>WebApplicationFactory&lt;Program&gt;.CreateHost</c> mutates
/// the <c>Admin__Password</c> / <c>ConnectionStrings__Library</c> process environment variables for
/// the duration of the call (Story056's <c>SafeTrackWebFactory</c>, Story058's
/// <c>SettingsApiWebFactory</c>, Story084's <c>StatusApiWebFactory</c>). The env-var technique is
/// required there — Program.cs reads both values before any <c>WebApplicationFactory</c> config
/// hook (<c>UseSetting</c>/<c>ConfigureAppConfiguration</c>) is visible to it — but env vars are
/// process-global, so two of these factories building a host on separate threads at the same moment
/// can each bake in the OTHER's value.
///
/// Putting every such spec class in this collection is the fix, not a workaround: xUnit never runs
/// two test classes that share a collection in parallel with each other, so their env-var-mutation
/// windows can no longer overlap. Classes outside this collection are unaffected and keep running in
/// parallel with everything else.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EnvVarMutatingWebFactoryCollection
{
    public const string Name = "EnvVarMutatingWebFactory";
}
