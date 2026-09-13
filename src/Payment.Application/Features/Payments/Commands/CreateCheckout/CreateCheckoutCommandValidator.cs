using FluentValidation;

namespace Payment.Application.Features.Payments.Commands.CreateCheckout;

public class CreateCheckoutCommandValidator : AbstractValidator<CreateCheckoutCommand>
{
    public CreateCheckoutCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("OrderId is required.");

        RuleFor(x => x.SuccessUrl)
            .NotEmpty()
            .WithMessage("SuccessUrl is required.");

        RuleFor(x => x.CancelUrl)
            .NotEmpty()
            .WithMessage("CancelUrl is required.");
    }
}
