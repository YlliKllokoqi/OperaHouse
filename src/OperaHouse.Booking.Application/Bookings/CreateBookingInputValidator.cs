using FluentValidation;

namespace OperaHouse.Booking.Application.Bookings;

public sealed class CreateBookingInputValidator
    : AbstractValidator<CreateBookingInput>
{
    public CreateBookingInputValidator()
    {
        RuleFor(input => input.PerformanceId)
            .NotEmpty()
            .WithMessage("PerformanceId is required.");

        RuleFor(input => input.CustomerEmail)
            .NotEmpty()
            .WithMessage("Customer email is required.")
            .EmailAddress()
            .WithMessage("Customer email must be a valid email address.")
            .MaximumLength(320)
            .WithMessage("Customer email cannot exceed 320 characters.");

        RuleFor(input => input.Seats)
            .GreaterThan(0)
            .WithMessage("Seats must be greater than zero.");
    }
}
