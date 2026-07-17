namespace GenWave.Example.Domain;

/// <summary>
/// Entity template: a domain object with identity and a guarded lifecycle.
/// Identity (not field values) defines equality. State changes go through
/// methods that enforce invariants — no public setters, no invalid
/// instances. Adapt: rename, replace the state machine with your own.
/// </summary>
public sealed class ScheduledSegment
{
    public SegmentId Id { get; }
    public string Title { get; private set; }
    public TimeSpan Duration { get; private set; }
    public SegmentStatus Status { get; private set; }

    private ScheduledSegment(SegmentId id, string title, TimeSpan duration)
    {
        Id = id;
        Title = title;
        Duration = duration;
        Status = SegmentStatus.Pending;
    }

    public static ScheduledSegment Create(SegmentId id, string title, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");
        }

        return new ScheduledSegment(id, title.Trim(), duration);
    }

    public void MarkPlaying()
    {
        if (Status != SegmentStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot start segment {Id} from state {Status}.");
        }

        Status = SegmentStatus.Playing;
    }

    public void MarkCompleted()
    {
        if (Status != SegmentStatus.Playing)
        {
            throw new InvalidOperationException($"Cannot complete segment {Id} from state {Status}.");
        }

        Status = SegmentStatus.Completed;
    }

    public override string ToString() => $"{Id} '{Title}' [{Status}]";

    // Identity-based equality: two entities are the same iff IDs match.
    public override bool Equals(object? obj) =>
        obj is ScheduledSegment other && Id.Equals(other.Id);

    public override int GetHashCode() => Id.GetHashCode();
}
