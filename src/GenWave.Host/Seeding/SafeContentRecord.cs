namespace GenWave.Host.Seeding;

/// <summary>
/// One station-voice item <see cref="SafeLoopSeeder"/> rendered, and the voice it was rendered in (SPEC F219.22).
/// </summary>
/// <param name="MediaId">The <c>library.media</c> row airing the item.</param>
/// <param name="Marker">The <see cref="SafeContentMarker"/> the file was made under; empty when it is unknown (a row
/// seeded before the sweep existed), which always reads as stale once.</param>
/// <param name="PendingRetireMediaId">The row a finished swap replaced and has not yet taken off air. A later tick
/// finishes the retire, so a failed flip never leaves two takes airing for good.</param>
public sealed record SafeContentRecord(long MediaId, string Marker, long? PendingRetireMediaId = null);
