namespace GenWave.MediaLibrary.Scan;

/// <summary>
/// One supported file as seen by the stat-only discovery walk (PRD §5.1): its engine-visible path,
/// detected format, and the <c>(size, mtime)</c> change signal. Built without opening the file.
/// </summary>
sealed record MediaFile(string Path, string Format, long SizeBytes, DateTime Mtime);
