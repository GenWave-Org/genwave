namespace GenWave.Example.Domain;

/// <summary>
/// Value-object template: an immutable, validated domain value.
/// Pattern: sealed record + private constructor + static factory that
/// validates. An instance that exists is valid — callers never re-check.
/// Adapt: rename, change the value type, replace the validation rules.
/// </summary>
public sealed record StreamMountPoint
{
    public string Value { get; }

    private StreamMountPoint(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Validating factory. Returns a failure result for expected bad input
    /// (user/config data); throws only for programmer error.
    /// </summary>
    public static Result<StreamMountPoint> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Result<StreamMountPoint>.Failure("Mount point is required.");
        }

        var trimmed = input.Trim();
        if (!trimmed.StartsWith('/'))
        {
            return Result<StreamMountPoint>.Failure($"Mount point must start with '/': '{trimmed}'");
        }

        if (trimmed.Length > 64)
        {
            return Result<StreamMountPoint>.Failure("Mount point exceeds 64 characters.");
        }

        return Result<StreamMountPoint>.Success(new StreamMountPoint(trimmed));
    }

    public override string ToString() => Value;
}
