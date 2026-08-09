namespace GenWave.Core.Abstractions;

/// <summary>
/// The default <see cref="IContextCacheRootProvider"/> binding: every call reads back a blank root —
/// mirrors <see cref="NoOpStationLocationProvider"/>'s own "shared instance for non-DI construction"
/// idiom one seam over (SPEC F109.2). A blank <see cref="Root"/> is a legal, fail-closed input to any
/// disk-caching provider (never a caller-visible fault), so this binding never has to be swapped in
/// just to keep a provider constructible — it is the correct answer for "no cache root wired yet", not
/// merely a placeholder for one.
/// </summary>
public sealed class NoOpContextCacheRootProvider : IContextCacheRootProvider
{
    /// <summary>Shared instance for non-DI construction (Core types, tests).</summary>
    public static readonly NoOpContextCacheRootProvider Instance = new();

    /// <inheritdoc/>
    public string Root => string.Empty;
}
