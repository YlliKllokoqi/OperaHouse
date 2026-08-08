using FluentValidation.Results;

namespace OperaHouse.Booking.Application.Common;

public static class ValidationResultExtensions
{
    public static ApplicationError ToApplicationError(
        this ValidationResult validationResult,
        string code,
        string message)
    {
        var errors = validationResult.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(error => error.ErrorMessage)
                    .Distinct()
                    .ToArray());

        return new ApplicationError(
            code,
            message,
            ApplicationErrorType.Validation,
            errors);
    }
}
