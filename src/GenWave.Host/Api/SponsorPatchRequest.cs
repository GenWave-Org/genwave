namespace GenWave.Host.Api;

/// <summary>
/// <c>PATCH /api/sponsors/{id}</c>'s own request body (SPEC F171.3; STORY-408; PLAN T434) — a sparse
/// edit, the <c>GenWave.Core.Domain.SponsorEdit</c>/<c>MediaController.Patch</c> precedent:
/// <see langword="null"/> means "leave this field unchanged". <see cref="Name"/> is sparse the same
/// way, but a pack-owned sponsor refuses ANY attempt to change it
/// (<c>SponsorWriteResult.NamePackOwned</c>, enforced by the store — see
/// <c>SponsorEdit</c>'s own remarks). No <c>packSlug</c> field exists here at all — pack ownership
/// itself is never editable through this surface.
/// </summary>
/// <param name="Name">The sponsor's new name, or <see langword="null"/> to leave it unchanged.</param>
/// <param name="Tagline">A short pitch line, or <see langword="null"/> to leave it unchanged (a blank
/// string clears it back to <see langword="null"/> — see <c>SponsorEdit</c>'s own remarks).</param>
/// <param name="About">A longer description — same null/blank convention as <see cref="Tagline"/>.</param>
/// <param name="Phone">A contact phone number — same null/blank convention as <see cref="Tagline"/>.</param>
/// <param name="Address">A physical address — same null/blank convention as <see cref="Tagline"/>.</param>
/// <param name="Website">A <c>http(s)://</c> URL — same null/blank convention as <see cref="Tagline"/>.</param>
/// <param name="Tone">A house tone hint — same null/blank convention as <see cref="Tagline"/>.</param>
public sealed record SponsorPatchRequest(
    string? Name,
    string? Tagline,
    string? About,
    string? Phone,
    string? Address,
    string? Website,
    string? Tone);
