using GenWave.Core.Abstractions;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Mutable <see cref="ICopyBoundsProvider"/> double (gh-#253, mirrors
/// <see cref="FakeBoundaryBiasProvider"/> one seam over). Set <see cref="Max"/> between calls to
/// simulate a live <c>Llm:MaxCopyChars</c> edit without standing up an options stack.
/// </summary>
sealed class FakeCopyBoundsProvider(int max) : ICopyBoundsProvider
{
    public int Max { get; set; } = max;

    public int MaxCopyChars => Max;
}
