// Extracted from Story345_PaWireProof.cs's own file-scoped PaWireProofSupport (T352 review, PLAN
// T352, the same "extract once a second caller needs it" precedent LlmCompletionsStub.cs's and
// EphemeralStationDatabase.cs's own header comments already document): Story364_TheGateRules-
// OnThePreviewWire.cs needs exactly these two members — a `file`-scoped type genuinely cannot cross
// files, so they move here rather than a second, byte-identical copy. Story345_PaWireProof.cs's own
// PaWireProofSupport now delegates to these instead of keeping its own bodies.
//
// T352 review round 2 (HIGH-1): AnnouncementRequest() no longer bakes a "GWAV 108.8" literal —
// STORY-364's own F138.8 exhibits exist to prove the station's own name is exempt from the fact
// gate, which only means something if the request handed to the gate carries whatever name the
// factory ACTUALLY configured (Station:Name), not a string this helper invented independently. It
// reads live off the container instead, the exact seam PersonaController.Preview itself uses
// (IOptionsMonitor<StationOptions> -> station.Name/.Voice/.Id) — so a caller's own
// UseSetting("Station:Name", ...) is no longer inert: change that setting and every SegmentRequest
// this helper builds for that factory changes with it (see Story364's own TheDriftedStationNameArc).

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// The two smallest pieces of test plumbing every announcement-lane wire proof in this project needs
/// — a session login and the minimal <see cref="SegmentRequest"/> shape the announcement copy writer
/// takes. <see cref="AnnouncementRequest"/> is deliberately NOT a set of literal parameters: it reads
/// <see cref="StationOptions"/> off the SAME factory whose services the caller already resolved
/// everything else from (<c>IAnnouncementSource</c>, <c>IAnnouncementCopyWriter</c>, ...), so a
/// caller's own <c>Station:Name</c>/<c>Station:Voice</c>/<c>Station:Id</c> override actually reaches
/// the request the truth gate checks — never a second, independently-typed copy of those three
/// values that could silently drift from what the factory really booted with.
/// </summary>
internal static class AnnouncementWireSupport
{
    public static async Task LoginAsync(HttpClient client, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { password });
        if (response.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {response.StatusCode}");
    }

    /// <summary>The SAME minimal SegmentRequest shape Story358_AnnouncementFlavorEndToEnd.cs's own
    /// AnnouncementRequest() helper builds — station voice/name/id, no track, "now" — except every
    /// station value is read live from <paramref name="services"/>'s own <see cref="StationOptions"/>
    /// (the <c>PersonaController.Preview</c> seam) rather than a literal, so it can never drift from
    /// whatever the caller's factory actually configured.</summary>
    public static SegmentRequest AnnouncementRequest(IServiceProvider services)
    {
        var station = services.GetRequiredService<IOptionsMonitor<StationOptions>>().CurrentValue;
        return new(SegmentKind.Announcement, station.Voice, station.Name, Track: null, DateTimeOffset.UtcNow, station.Id);
    }
}
