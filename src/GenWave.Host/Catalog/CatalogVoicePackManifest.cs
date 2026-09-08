namespace GenWave.Host.Catalog;

/// <summary>
/// A voice pack's manifest content (SPEC F164.1/F164.5, STORY-395/396/398, PLAN T413) — mirrors
/// <see cref="CatalogAvatarPackManifest"/>'s own "ephemeral, hardened, null-tolerant" shape for a
/// third assets-carrying kind: a display <see cref="PackName"/>, the declared <see cref="Engine"/>
/// (compared against the station's <c>PrimaryVoiceEngine</c> by <c>Api.VoicePackController</c>, never
/// by this type or its serializer), the pack's shared <see cref="Preview"/> clip file name, and its
/// <see cref="Voices"/> roster. <c>synthetic</c>/<c>sourceRef</c> (SPEC F164.3) are gate conditions
/// <see cref="CatalogVoicePackManifestSerializer"/> enforces at parse time and never surfaces as
/// properties here — a manifest that reaches this type has already proven both.
/// </summary>
public sealed record CatalogVoicePackManifest(
    string PackName,
    string Engine,
    string Preview,
    IReadOnlyList<CatalogVoicePackVoice> Voices)
{
    /// <summary>
    /// Structural equality over <see cref="Voices"/> — mirrors <see cref="CatalogAvatarPackManifest"/>'s
    /// own remarks: the compiler-synthesized record equality would otherwise compare
    /// <see cref="IReadOnlyList{T}"/> by REFERENCE.
    /// </summary>
    public bool Equals(CatalogVoicePackManifest? other) =>
        other is not null &&
        string.Equals(PackName, other.PackName, StringComparison.Ordinal) &&
        string.Equals(Engine, other.Engine, StringComparison.Ordinal) &&
        string.Equals(Preview, other.Preview, StringComparison.Ordinal) &&
        Voices.SequenceEqual(other.Voices);

    /// <inheritdoc cref="Equals(CatalogVoicePackManifest?)"/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PackName, StringComparer.Ordinal);
        hash.Add(Engine, StringComparer.Ordinal);
        hash.Add(Preview, StringComparer.Ordinal);
        foreach (var voice in Voices)
            hash.Add(voice);

        return hash.ToHashCode();
    }
}

/// <summary>
/// One voice a <see cref="CatalogVoicePackManifest"/> declares. <see cref="File"/> is ALWAYS
/// <c>VoiceId + ".pt"</c> (PLAN T412's flat-layout ruling) whether the wire manifest named it
/// explicitly or the serializer derived it — see
/// <see cref="CatalogVoicePackManifestSerializer"/>'s own remarks for why an explicit, mismatched
/// <c>file</c> fails the whole manifest rather than being silently corrected.
/// </summary>
public sealed record CatalogVoicePackVoice(
    string VoiceId, string File, string? Gender, string? Age, IReadOnlyList<CatalogVoicePackBlend>? Blend)
{
    /// <summary>Structural equality over the optional <see cref="Blend"/> list — see
    /// <see cref="CatalogVoicePackManifest.Equals(CatalogVoicePackManifest?)"/>'s own remarks.</summary>
    public bool Equals(CatalogVoicePackVoice? other) =>
        other is not null &&
        string.Equals(VoiceId, other.VoiceId, StringComparison.Ordinal) &&
        string.Equals(File, other.File, StringComparison.Ordinal) &&
        string.Equals(Gender, other.Gender, StringComparison.Ordinal) &&
        string.Equals(Age, other.Age, StringComparison.Ordinal) &&
        (Blend, other.Blend) switch
        {
            (null, null) => true,
            (not null, not null) => Blend.SequenceEqual(other.Blend),
            _ => false,
        };

    /// <inheritdoc cref="Equals(CatalogVoicePackVoice?)"/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(VoiceId, StringComparer.Ordinal);
        hash.Add(File, StringComparer.Ordinal);
        hash.Add(Gender, StringComparer.Ordinal);
        hash.Add(Age, StringComparer.Ordinal);
        if (Blend is not null)
            foreach (var blend in Blend)
                hash.Add(blend);

        return hash.ToHashCode();
    }
}

/// <summary>One voice this pack's own <see cref="CatalogVoicePackVoice.Blend"/> mixes in — descriptive
/// metadata only (PLAN T413 ships no blending playback, just the validated shape for storage); a real
/// blend renderer is future work.</summary>
/// <param name="VoiceId">The blended-in voice id — validated the same way a top-level voice id is.</param>
/// <param name="Weight">The blend weight, always in the range <c>(0, 1]</c>.</param>
public sealed record CatalogVoicePackBlend(string VoiceId, double Weight);
