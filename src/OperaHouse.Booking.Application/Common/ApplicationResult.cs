namespace OperaHouse.Booking.Application.Common;

public sealed class ApplicationResult<T>
{
    private ApplicationResult(T value)
    {
        Value = value;
    }

    private ApplicationResult(ApplicationError error)
    {
        Error = error;
    }

    public T? Value { get; }

    public ApplicationError? Error { get; }

    public bool IsSuccess => Error is null;

    public static ApplicationResult<T> Success(T value)
    {
        return new ApplicationResult<T>(value);
    }

    public static ApplicationResult<T> Failure(ApplicationError error)
    {
        return new ApplicationResult<T>(error);
    }
}
