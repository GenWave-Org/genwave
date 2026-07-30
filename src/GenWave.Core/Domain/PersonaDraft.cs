namespace GenWave.Core.Domain;

/// <summary>
/// Caller-supplied fields for creating or updating a <see cref="Persona"/> (SPEC F35.1, STORY-118).
/// Groups the writable fields into one parameter so
/// <see cref="Abstractions.IPersonaStore.CreateAsync"/>/<see cref="Abstractions.IPersonaStore.UpdateAsync"/>
/// stay within the house's ≤3-parameter guidance. <paramref name="Voice"/> of <c>""</c> means
/// "use the station default" — see <see cref="Persona.Voice"/>.
/// </summary>
/// <param name="Soul">
/// Optional direct edit of the persona card's <see cref="PersonaCard.Soul"/> (gh-#256): a
/// catalog-hired persona's narrative lives in the card (with its <c>Style:</c> line embedded), not
/// in the legacy <paramref name="Backstory"/>/<paramref name="Style"/> columns — the editor submits
/// the soul text verbatim for those personas. <see langword="null"/> (or blank) means "not editing
/// the soul": the store falls back to the legacy rebuild-from-backstory/style behavior, preserving
/// an existing non-empty soul when that rebuild would be empty.
/// </param>
public sealed record PersonaDraft(string Name, string Backstory, string Style, string Voice, string? Soul = null);
