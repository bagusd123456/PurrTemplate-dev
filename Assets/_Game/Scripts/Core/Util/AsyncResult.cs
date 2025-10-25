/// <summary>
/// Represents the result of an asynchronous operation.
/// </summary>
public readonly struct AsyncResult
{
    public AsyncResult(bool isSuccess, bool isCanceled, string message, int errorCode = 0)
    {
        IsSuccess = isSuccess;
        IsCanceled = isCanceled;
        Message = message;
        ErrorCode = errorCode;
    }

    public readonly bool IsSuccess;
    public readonly bool IsCanceled;
    public readonly string Message;
    public readonly int ErrorCode;

    public bool IsFail => !IsSuccess;
    public bool IsFailOrCanceled => !IsSuccess || IsCanceled;

    public static AsyncResult Success() => new(true, false, string.Empty);
    public static AsyncResult Fail(string message, int errorCode = 0) => new(false, false, message, errorCode);
    public static AsyncResult Fail(int errorCode) => new(false, false, errorCode.ToString(), errorCode);
    public static AsyncResult Cancel(string message = "") => new(false, true, message);
}

/// <summary>
/// Represents the result of an asynchronous operation with a return value.
/// </summary>
public readonly struct AsyncResult<T>
{
    public AsyncResult(T result, bool isSuccess, bool isCanceled, string message, int errorCode = 0)
    {
        Result = result;
        IsSuccess = isSuccess;
        IsCanceled = isCanceled;
        Message = message;
        ErrorCode = errorCode;
    }

    public readonly T Result;
    public readonly bool IsSuccess;
    public readonly bool IsCanceled;
    public readonly string Message;
    public readonly int ErrorCode;

    public bool IsFail => !IsSuccess;
    public bool IsFailOrCanceled => !IsSuccess || IsCanceled;

    public static AsyncResult<T> Success(T result) => new(result, true, false, string.Empty);
    public static AsyncResult<T> Fail(string message, int errorCode = 0) => new(default!, false, false, message, errorCode);
    public static AsyncResult<T> Fail(int errorCode) => new(default!, false, false, errorCode.ToString(), errorCode);
    public static AsyncResult<T> Cancel(string message = "") => new(default!, false, true, message);
    public static AsyncResult<T> Cancel(T result, string message = "") => new(result, false, true, message);

    public static implicit operator AsyncResult(AsyncResult<T> result) =>
        new(result.IsSuccess, result.IsCanceled, result.Message, result.ErrorCode);

    public static AsyncResult<T> Fail<T>(object errorCode) where T : class
    {
        return new AsyncResult<T>(default!, false, false, errorCode.ToString() ?? string.Empty);
    }
}
