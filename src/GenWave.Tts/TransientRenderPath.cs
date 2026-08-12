namespace GenWave.Tts;

/// <summary>
/// Composes a fresh, per-call-unique path under <see cref="TtsOptions.CacheRoot"/> for an engine
/// adapter's raw synthesis write (T138 fix, gh-#161 wire smoke, SPEC F98.2-as-amended — see the
/// class remarks below for the ruling). The ONE seam every engine adapter's transient write shares
/// — <see cref="KokoroTtsSynthesizer"/>, <see cref="KokoroFallbackRenderer"/>,
/// <see cref="PiperTtsSynthesizer"/> — mirroring <see cref="PronunciationRuleSet.FromContext"/>'s
/// precedent (T137 review): one conversion/composition seam every caller resolves through, so a
/// future change to the naming scheme is made once, not N times in step.
///
/// <para>
/// <b>Root cause, stated once (the four call sites below no longer repeat it):</b> every one of
/// these three adapters used to name its transient write CONTENT-ADDRESSED — a deterministic hash
/// of (speech, voice[, endpoint]). That file is never read back by its own hash: it is always
/// either moved into <see cref="TtsSegmentSource"/>'s own final, differently-keyed cache slot via
/// <c>File.Move</c>, or deleted outright by <see cref="TtsPreviewController"/>/
/// <see cref="SafeSegmentAuthor"/> after streaming. The hash was write-only — nothing ever read a
/// file back BY that hash — so it bought no caching benefit whatsoever (SPEC F98.2, amended: there
/// is exactly ONE render cache in this system, <see cref="TtsSegmentSource"/>'s own station-scoped
/// one; T140's pace-key obligation is that cache alone, never a second "engine file cache" — the
/// engine's own write path is transient scratch space, full stop). What the hash DID buy was a
/// live-observed bug: two concurrent renders of IDENTICAL (speech, voice) — reachable any time an
/// evergreen template phrase or a degraded-LLM canned reply repeats verbatim across two segments
/// the Orchestrator kicks off back-to-back with nothing awaited in between (SPEC F44.2) — collided
/// on the exact same transient path. Whichever render's <c>File.Move</c> won the race deleted the
/// file out from under the other, whose own <c>File.Move</c> then threw
/// <see cref="FileNotFoundException"/> on a path that had existed a moment earlier (WARN "TTS
/// render failed for LeadIn/af_nova").
/// </para>
///
/// <para>
/// The fix: a fresh <see cref="Guid"/> per call, never a function of what is being rendered — two
/// renders of identical content can no longer share a filename to race on, structurally. Every
/// call site below adds only what makes its own transient namespace distinct (a subfolder, so a
/// Piper write can never collide with a concurrent Kokoro one even by coincidence) — the Guid
/// itself is the whole collision-safety argument.
/// </para>
/// </summary>
static class TransientRenderPath
{
    /// <summary>
    /// <paramref name="subfolder"/> is <see langword="null"/> for the primary Kokoro path (writes
    /// directly under <see cref="TtsOptions.CacheRoot"/>) and a fixed literal for every other
    /// engine adapter (<c>"piper"</c>, <c>"fallback-kokoro"</c>) — enough to keep two DIFFERENT
    /// adapters' transient namespaces apart; the Guid is what keeps two calls to the SAME adapter
    /// apart. <c>"N"</c> formatting (no dashes/braces) keeps the filename plain; it carries no
    /// meaning beyond "unique for this one write".
    /// </summary>
    public static string For(TtsOptions cfg, string? subfolder = null) =>
        subfolder is null
            ? Path.Combine(cfg.CacheRoot, $"{Guid.NewGuid():N}.{cfg.Format}")
            : Path.Combine(cfg.CacheRoot, subfolder, $"{Guid.NewGuid():N}.{cfg.Format}");
}
