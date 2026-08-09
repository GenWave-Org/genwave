using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Tts;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IContextCacheRootProvider"/> seam (SPEC F109.2, PLAN T226):
/// reuses the SAME writable volume <see cref="TtsOptions.CacheRoot"/> already points at (Kokoro's
/// render cache, <c>TtsSegmentSource</c>'s blurb cache) rather than minting a second cache-root
/// config knob — <c>HistoryContextProvider</c>'s day files land under
/// <c>{CacheRoot}/context/history/</c>, one more subdirectory alongside <c>tts/</c>/<c>blurbs/</c>/
/// <c>piper/</c>/<c>fallback-kokoro/</c> under the same volume.
///
/// Wraps <see cref="IOptionsMonitor{TOptions}"/> and re-reads <c>CurrentValue</c> on every call —
/// nothing is cached here, the same discipline every sibling provider in this folder follows.
/// </summary>
sealed class OptionsMonitorContextCacheRootProvider(IOptionsMonitor<TtsOptions> ttsMonitor)
    : IContextCacheRootProvider
{
    public string Root => ttsMonitor.CurrentValue.CacheRoot;
}
