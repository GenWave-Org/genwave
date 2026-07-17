namespace GenWave.Example.Domain;

/// <summary>
/// Result type for expected, recoverable failures — the sad paths that
/// appear in specs. Exceptions remain for bugs and environment failures.
/// An instance is either Success (Value set) or Failure (Error set);
/// the private constructor makes a third state unrepresentable.
/// </summary>
public sealed class Result<T>
{
    private readonly T? value;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string Error { get; }

    /// <summary>Throws if accessed on a failure — check IsSuccess first or use Match.</summary>
    public T Value => IsSuccess
        ? value ?? throw new InvalidOperationException("Success result holds no value.")
        : throw new InvalidOperationException($"Cannot read Value of a failed result: {Error}");

    private Result(bool isSuccess, T? value, string error)
    {
        IsSuccess = isSuccess;
        this.value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, string.Empty);

    public static Result<T> Failure(string error) => new(false, default, error);

    /// <summary>Transform the value if successful; failures pass through.</summary>
    public Result<TNext> Map<TNext>(Func<T, TNext> transform) =>
        IsSuccess ? Result<TNext>.Success(transform(Value)) : Result<TNext>.Failure(Error);

    /// <summary>Chain another result-producing step; failures short-circuit.</summary>
    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> next) =>
        IsSuccess ? next(Value) : Result<TNext>.Failure(Error);

    /// <summary>Force both paths to be handled — the safest way to consume.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);

    public override string ToString() => IsSuccess ? $"Success({Value})" : $"Failure({Error})";
}
