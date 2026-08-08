using Microsoft.AspNetCore.Mvc;
using OperaHouse.Booking.Application.Common;

namespace OperaHouse.Booking.Api.Errors;

public static class ApplicationErrorMapper
{
    public static ActionResult ToActionResult(
        this ControllerBase controller,
        ApplicationError error)
    {
        if (error.Type == ApplicationErrorType.Validation)
        {
            var problemDetails = new ValidationProblemDetails(
                error.ValidationErrors is null
                    ? new Dictionary<string, string[]>()
                    : new Dictionary<string, string[]>(
                        error.ValidationErrors))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Detail = error.Message
            };

            problemDetails.Extensions["code"] = error.Code;

            return controller.BadRequest(problemDetails);
        }

        var statusCode = error.Type switch
        {
            ApplicationErrorType.NotFound =>
                StatusCodes.Status404NotFound,
            ApplicationErrorType.Conflict =>
                StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = error.Type switch
            {
                ApplicationErrorType.NotFound => "Resource not found",
                ApplicationErrorType.Conflict => "Request conflict",
                _ => "Request failed"
            },
            Detail = error.Message
        };

        problem.Extensions["code"] = error.Code;

        return new ObjectResult(problem)
        {
            StatusCode = statusCode
        };
    }
}
