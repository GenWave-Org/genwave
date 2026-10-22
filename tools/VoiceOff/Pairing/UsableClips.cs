using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Pairing;

/// <summary>
/// Which clips of a run can be paired: the timing record says ok, the wav is there, and its header reads. A missing record, a missing
/// file, a failed render and an unreadable wav all answer false. When a clip was rendered twice, the later record wins.
/// </summary>
internal sealed class UsableClips
{
    private readonly RunLayout layout;
    private readonly CanonicalClips canonical;
    private readonly Dictionary<(string Engine, RenderLane Lane, string Line), bool> okByClip = [];

    internal UsableClips(RunLayout layout, CanonicalClips canonical, IEnumerable<TimingRecord> records)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(records);
        this.layout = layout;
        this.canonical = canonical;
        foreach (TimingRecord record in records)
        {
            okByClip[(record.Engine, record.Lane, record.Line)] = record.Ok;
        }
    }

    internal bool IsUsable(string engine, RenderLane lane, string lineId) =>
        okByClip.GetValueOrDefault((engine, lane, lineId)) && canonical.Get(layout.WavPath(engine, lane, lineId)) is not null;
}
