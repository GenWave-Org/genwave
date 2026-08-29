namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L5's named, seeded constant (SPEC F105.4, STORY-292 AC1): the reserved/graduated-namespace list
/// the Host graduation rule enforces. Seeded today with the three born-outside reservations the
/// ruling names explicitly — <c>IContextProvider</c> (gh-#378), the Ads library (gh-#380), and the
/// Library Gardener (SPEC F155.2, STORY-380, PLAN T357, gh-#529) must not land their logic in
/// <c>GenWave.Host</c>. The graduated category (a subsystem that has since moved OUT
/// of Host per the ladder — Theming → Catalog proxy → Requests → Stats) starts empty and grows the
/// same way: one more entry, appended here, the next time a graduation ruling lands (ARCHITECTURE.md
/// "The Host graduation rule" — "enforce via L5 so 'on demand' can't be forgotten"). The detector
/// itself is <see cref="HostNamespaceTripwire"/>.
/// </summary>
internal static class HostReservedNamespaces
{
    public static readonly IReadOnlyList<HostNamespaceReservation> Entries = new[]
    {
        new HostNamespaceReservation(
            "GenWave.Host.Context",
            "F105.4",
            "this subsystem is born OUTSIDE Host (gh-#378)"),
        new HostNamespaceReservation(
            "GenWave.Host.Ads",
            "F105.4",
            "this subsystem is born OUTSIDE Host (gh-#380)"),
        new HostNamespaceReservation(
            "GenWave.Host.Gardener",
            "F155.2",
            "the Library Gardener is born OUTSIDE Host, in GenWave.MediaLibrary/Garden (gh-#529)"),
    };
}
