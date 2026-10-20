namespace GenWave.Core.Domain;

/// <summary>One authored <c>library.media</c> row the retired-audio sweep may remove (gh-#3): its id and
/// the file it points at.</summary>
public sealed record AuthoredMediaFile(long Id, string Path);
