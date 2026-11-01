namespace OperaHouse.Contracts.Results;

public sealed record ApplicationError(
    string Code,
    string Message,
    ApplicationErrorType Type,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static ApplicationError FromValidation(string code, string message,
        IEnumerable<(string PropertyName, string ErrorMessage)> failures) =>
        new(code, message, ApplicationErrorType.Validation,
            failures.GroupBy(error => error.PropertyName).ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).Distinct().ToArray()));
}
