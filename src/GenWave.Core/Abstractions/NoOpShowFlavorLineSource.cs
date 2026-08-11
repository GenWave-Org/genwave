namespace GenWave.Core.Abstractions;

using GenWave.Core.Domain;

/// <summary>
/// The default <see cref="IShowFlavorLineSource"/> binding (SPEC F116.3, STORY-308): always answers
/// "no line due" — mirrors <see cref="NoOpContextPatterFactSource"/>'s own "safe default until a real
/// seam is wired" idiom one seam over. <c>GenWave.Tts.TtsServiceCollectionExtensions</c> registers this
/// with <c>TryAddSingleton</c> so the Host's real <c>GenWave.Orchestration.ShowFlavorLineGate</c>
/// binding, once wired, overrides it without <c>GenWave.Tts</c> ever needing a project reference to
/// <c>GenWave.Orchestration</c> (an L1 project one layer further out) at all. Until that wiring lands —
/// or for any composition that never registers a show-flavor gate at all (a unit test, a station with
/// no schedule store configured) — every render behaves exactly as it did before F116.3: the patter
/// lane's own byte-identical-with-no-line golden (PLAN T249) is what pins that this default is inert,
/// not merely absent.
/// </summary>
public sealed class NoOpShowFlavorLineSource : IShowFlavorLineSource
{
    /// <summary>Shared instance for non-DI construction (Core types, tests).</summary>
    public static readonly NoOpShowFlavorLineSource Instance = new();

    /// <inheritdoc/>
    public ShowFlavorFact? TryTakeDueShowLine() => null;
}
