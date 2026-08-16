namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Dapper's flat projection of one <c>station.persona_avatar</c> row (mapped by the globally-enabled
/// <c>MatchNamesWithUnderscores</c>, same as <c>Catalog.MediaRow</c>). <see cref="Source"/> arrives as
/// its raw text column rather than parsed directly into <c>PersonaAvatarSource</c> — mirrors
/// <c>PersonaTasteRow</c>'s own reasoning: <see cref="PersonaAvatarRepository"/> does the read-side
/// enum parse itself, via an exhaustive throwing switch (<c>ToSource</c>), rather than trusting Dapper's
/// own implicit string-to-enum conversion.
/// </summary>
sealed record PersonaAvatarRow(
    long PersonaId,
    byte[] Bytes,
    int ByteSize,
    string Sha256,
    string Token,
    string Source,
    string? ImportedFrom,
    DateTime UpdatedAt);
