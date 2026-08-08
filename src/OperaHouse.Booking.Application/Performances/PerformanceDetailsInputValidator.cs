using FluentValidation;

namespace OperaHouse.Booking.Application.Performances;

public sealed class PerformanceDetailsInputValidator
    : AbstractValidator<PerformanceDetailsInput>
{
    private const int MaximumCapacity = 100_000;

    public PerformanceDetailsInputValidator(TimeProvider timeProvider)
    {
        RuleFor(input => input.Title)
            .NotEmpty()
            .WithMessage("A title is required.")
            .MaximumLength(200)
            .WithMessage("The title cannot exceed 200 characters.");

        RuleFor(input => input.Venue)
            .NotEmpty()
            .WithMessage("A venue is required.")
            .MaximumLength(200)
            .WithMessage("The venue cannot exceed 200 characters.");

        RuleFor(input => input.StartsAt)
            .Must(startsAt => startsAt > timeProvider.GetUtcNow())
            .WithMessage("The performance must start in the future.");

        RuleFor(input => input.Capacity)
            .InclusiveBetween(1, MaximumCapacity)
            .WithMessage(
                $"Capacity must be between 1 and {MaximumCapacity}.");
    }
}
