namespace GenWave.VoiceOff.Audio;

/// <summary>
/// The outcome of matching one render (SPEC F214.4 as amended by F217.5). A failed match is a failed render: it leaves no
/// file behind and is never paired. <paramref name="MeasuredLufs"/> is the verified integrated loudness
/// of the output when one was taken. <paramref name="Linear"/> records whether loudnorm applied a pure gain
/// (true) or fell back to its dynamic, compressing mode (when a linear gain would break the true-peak ceiling
/// or the clip has no loudness range); the verify still gates on LUFS, the mode is kept for the report.
/// </summary>
internal sealed record LoudnessMatchResult(bool Ok, string? Error, double? MeasuredLufs, bool Linear = false)
{
    internal static LoudnessMatchResult Success(double measuredLufs, bool linear = false) => new(true, null, measuredLufs, linear);

    internal static LoudnessMatchResult Failure(string error, double? measuredLufs = null) => new(false, error, measuredLufs);
}
