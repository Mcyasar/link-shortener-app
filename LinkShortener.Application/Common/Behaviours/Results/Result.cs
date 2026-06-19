namespace LinkShortener.Application.Common.Results;

public class Result<TValue>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public TValue? Value { get; }
    public Error? Error { get; }

    protected Result(TValue? value, bool isSuccess, Error? error)
    {
        if (isSuccess && error != null || !isSuccess && error == null)
        {
            throw new ArgumentException("Invalid Result creation.", nameof(error));
        }

        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<TValue> Success(TValue value) => new(value, true, null);
    public static Result<TValue> Failure(Error error) => new(default, false, error);
}

public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    protected Result(bool isSuccess, Error? error) => (IsSuccess, Error) = (isSuccess, error);
    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);
}