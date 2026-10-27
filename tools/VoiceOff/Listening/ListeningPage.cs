using System.Reflection;

namespace GenWave.VoiceOff.Listening;

/// <summary>The embedded one-file listening page (<c>page.html</c>); <c>serve</c> hands it out.</summary>
internal static class ListeningPage
{
    private const string ResourceName = "page.html";

    internal static string Html()
    {
        using Stream stream = typeof(ListeningPage).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The listening page is not embedded in this build.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
