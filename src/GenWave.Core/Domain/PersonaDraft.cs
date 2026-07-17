namespace GenWave.Core.Domain;

/// <summary>
/// Caller-supplied fields for creating or updating a <see cref="Persona"/> (SPEC F35.1, STORY-118).
/// Groups the four writable fields into one parameter so
/// <see cref="Abstractions.IPersonaStore.CreateAsync"/>/<see cref="Abstractions.IPersonaStore.UpdateAsync"/>
/// stay within the house's ≤3-parameter guidance. <paramref name="Voice"/> of <c>""</c> means
/// "use the station default" — see <see cref="Persona.Voice"/>.
/// </summary>
public sealed record PersonaDraft(string Name, string Backstory, string Style, string Voice);
