namespace GenWave.Example.Configuration;

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

/// <summary>
/// Options-pattern template: one class per concern, data-annotation
/// validation for field rules, IValidateOptions for cross-field rules,
/// ValidateOnStart so bad config fails the boot — not a 2 AM request.
/// (One type per file in real code — shown together here as a template.)
///
/// Registration in Program.cs:
///   builder.Services
///       .AddOptions<StreamingOptions>()
///       .BindConfiguration(StreamingOptions.SectionName)
///       .ValidateDataAnnotations()
///       .ValidateOnStart();
///   builder.Services.AddSingleton<IValidateOptions<StreamingOptions>, StreamingOptionsValidator>();
/// </summary>
public sealed class StreamingOptions
{
    public const string SectionName = "Streaming";

    [Required]
    public string MountPoint { get; init; } = string.Empty;

    [Required, Url]
    public string IcecastUrl { get; init; } = string.Empty;

    [Range(32, 320)]
    public int BitrateKbps { get; init; } = 128;

    [Range(1, 64)]
    public int QueueCapacity { get; init; } = 8;
}

public sealed class StreamingOptionsValidator : IValidateOptions<StreamingOptions>
{
    public ValidateOptionsResult Validate(string? name, StreamingOptions options)
    {
        if (!options.MountPoint.StartsWith('/'))
        {
            return ValidateOptionsResult.Fail(
                $"{StreamingOptions.SectionName}:{nameof(options.MountPoint)} must start with '/': '{options.MountPoint}'");
        }

        return ValidateOptionsResult.Success;
    }
}
