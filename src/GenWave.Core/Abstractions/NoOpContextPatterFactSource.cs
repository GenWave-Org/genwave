namespace GenWave.Core.Abstractions;

using GenWave.Core.Domain;

/// <summary>
/// The default <see cref="IContextPatterFactSource"/> binding (SPEC F107.5, STORY-298): always
/// answers "no fact due" — mirrors <see cref="NoOpContextSettingsProvider"/>'s own "safe default
/// until a real seam is wired" idiom one seam over. <c>GenWave.Tts.TtsServiceCollectionExtensions</c>
/// registers this with <c>TryAddSingleton</c> so the T226 Host wiring, once it registers the real
/// <c>GenWave.Context.ContextPipeline</c> binding, overrides it without <c>GenWave.Tts</c> ever
/// needing a project reference to <c>GenWave.Context</c> (an L1 project one layer further out) at
/// all. Until that wiring lands — or for any composition that never registers a context pipeline at
/// all (a unit test, a station running with no context providers configured) — every render behaves
/// exactly as it did before F107: the patter lane's own byte-identical-with-no-fact golden (PLAN
/// T225) is what pins that this default is inert, not merely absent.
/// </summary>
public sealed class NoOpContextPatterFactSource : IContextPatterFactSource
{
    /// <summary>Shared instance for non-DI construction (Core types, tests).</summary>
    public static readonly NoOpContextPatterFactSource Instance = new();

    /// <inheritdoc/>
    public ContextPatterFact? TryTakeDuePatterFact() => null;
}
