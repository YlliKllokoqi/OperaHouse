using FluentValidation;

namespace OperaHouse.Booking.Application.Performances;

public sealed class CancelPerformanceInputValidator
    : AbstractValidator<CancelPerformanceInput>
{
    public CancelPerformanceInputValidator()
    {
        RuleFor(input => input.Reason)
            .NotEmpty()
            .WithMessage("A cancellation reason is required.")
            .MaximumLength(1_000)
            .WithMessage(
                "The cancellation reason cannot exceed 1000 characters.");
    }
}
