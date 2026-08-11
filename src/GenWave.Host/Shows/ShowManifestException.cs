namespace GenWave.Host.Shows;

/// <summary>
/// An imported show manifest failed load-time validation (SPEC F118.2, PLAN T254) — mirrors
/// <see cref="Theming.ThemeManifestException"/>. Thrown by <see cref="ShowManifestParser"/>, caught by
/// <see cref="Api.ShowsController.Import"/> and mapped to 400 (deserialization-as-validation, never an
/// unhandled 500).
/// </summary>
public sealed class ShowManifestException : Exception
{
    public ShowManifestException(string message)
        : base(message)
    {
    }
}
