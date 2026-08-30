using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// A scriptable <see cref="IFileTagReader"/> — every call returns the SAME configured
/// <see cref="FileTags"/>? (never a real file open), and every call is counted. Backs STORY-379's
/// retag diff facts (a canned file-tag reading, exactly like the pre-T381-review-N4 subject's own
/// <c>CurrentFileTags</c> field used to supply directly) AND T381 review N4's own "zero reads on a
/// refused retag" pin — <see cref="ReadCount"/> proves <see cref="GenWave.MediaLibrary.Garden.FileActions.FileActionPlanner"/>
/// never opens a subject the destination gate has already refused.
/// </summary>
public sealed class RecordingFileTagReader(FileTags? answer = null) : IFileTagReader
{
    public int ReadCount { get; private set; }

    public FileTags? TryRead(string path)
    {
        ReadCount++;
        return answer;
    }
}
