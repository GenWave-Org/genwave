namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// Expands a <see cref="SegmentRequest"/> into the spoken copy that will be
/// synthesized by the TTS engine.  Pure string interpolation — no I/O, no
/// external dependencies.
/// </summary>
public sealed class PatterTemplateRenderer
{
    /// <summary>
    /// Returns the patter text for <paramref name="request"/>.
    /// Null-<see cref="SegmentRequest.Track"/> cases produce safe fallback
    /// phrasings — never literal "null", never a <see cref="NullReferenceException"/>.
    /// </summary>
    public string Expand(SegmentRequest request) => request.Kind switch
    {
        SegmentKind.StationId    => $"You're listening to {request.StationName}.",
        SegmentKind.LeadIn       => request.Track switch
                                    {
                                        { Artist.Length: > 0 } t => $"Coming up: {t.Title} by {t.Artist}.",
                                        { } t                    => $"Coming up: {t.Title}.",
                                        null                     => "Coming up next.",
                                    },
        SegmentKind.BackAnnounce => request.Track switch
                                    {
                                        { Artist.Length: > 0 } t => $"That was {t.Title} by {t.Artist}.",
                                        { } t                    => $"That was {t.Title}.",
                                        null                     => "That was your last track.",
                                    },
        SegmentKind.TimeDate     => $"It's {request.LocalNow:h:mm tt} here on {request.StationName}.",
        _                        => throw new ArgumentOutOfRangeException(
                                        nameof(request.Kind), request.Kind, message: null),
    };
}
