namespace GenWave.Core.Domain;

/// <summary>
/// The <c>library.media</c> column a facet query enumerates (SPEC F52.1). Maps 1:1 to the
/// <c>GET /api/media/facets?field=artist|album|genre</c> query string value — parsing an unknown
/// string to this enum (and rejecting anything else with a 400) is the controller's job, mirroring
/// <see cref="VoteDirection"/>.
/// </summary>
public enum FacetField
{
    Artist,
    Album,
    Genre,
}
