namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// The cheap change-detection projection of a catalog row used by discovery (PRD §5.1): compare
/// <c>(path, size, mtime)</c> against what the scan stats on disk to classify each file as
/// new / changed / unchanged / missing. No file is opened to build this.
/// </summary>
sealed record MediaFingerprint(long Id, string Path, long SizeBytes, DateTime Mtime, string State);
