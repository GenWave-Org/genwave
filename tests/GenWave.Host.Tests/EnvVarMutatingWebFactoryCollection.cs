namespace GenWave.Host.Tests;

/// <summary>
/// xUnit collection for specs whose <c>WebApplicationFactory&lt;Program&gt;.CreateHost</c> mutates a
/// process environment variable for the duration of the call. As of STORY-196's env-var-race fix,
/// this is down to a single factory: Story164's <c>NoPasswordWebFactory</c>, which must prove
/// <c>Admin:Password</c> is genuinely ABSENT (not merely empty) — <c>UseSetting</c>/
/// <c>ConfigureHostConfiguration</c>-injected values sit at a lower configuration precedence than a
/// real environment variable (verified empirically), so they cannot force absence if the box already
/// exports <c>Admin__Password</c>; only nulling the real env var can. Every other spec in this test
/// project reaches Program.cs's composition-time config reads (e.g. <c>AddMediaLibrary</c>'s
/// <c>ConnectionStrings:Library</c>) via <c>ConfigureWebHost</c>'s <c>UseSetting</c> instead — a
/// per-instance value with no shared process state, so it cannot race with anything and needs no
/// collection membership.
///
/// Putting every process-env-mutating spec class in this collection is the fix, not a workaround:
/// xUnit never runs two test classes that share a collection in parallel with each other, so their
/// env-var-mutation windows can no longer overlap. Classes outside this collection are unaffected and
/// keep running in parallel with everything else.
///
/// The precedence flip's companion guard lives in
/// <c>DisableConfigFileWatchingModuleInitializer</c>: it nulls the known binding-form config keys
/// once before any test runs, so an ambient <c>Admin__Password</c>/<c>ConnectionStrings__*</c>
/// export on a dev box can never out-rank the factories' <c>UseSetting</c> values.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EnvVarMutatingWebFactoryCollection
{
    public const string Name = "EnvVarMutatingWebFactory";
}
