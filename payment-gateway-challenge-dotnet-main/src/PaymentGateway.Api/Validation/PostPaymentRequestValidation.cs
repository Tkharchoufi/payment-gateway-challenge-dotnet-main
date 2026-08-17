using FluentValidation;

using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Validation;

public class PostPaymentRequestValidator : AbstractValidator<PostPaymentRequest>
{
    public static readonly string[] SupportedCurrencies = ["GBP", "USD", "EUR"];

    public PostPaymentRequestValidator()
    {
        RuleFor(r => r.CardNumber)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Card number is required")
            .Matches("^[0-9]{14,19}$")
            .WithMessage("Card number must be 14-19 characters long and contain numeric characters only");

        RuleFor(r => r.ExpiryMonth)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Expiry month is required")
            .InclusiveBetween(1, 12).WithMessage("Expiry month must be between 1 and 12");

        RuleFor(r => r.ExpiryYear)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Expiry year is required")
            .InclusiveBetween(1, 9999).WithMessage("Expiry year must be a valid year");

        RuleFor(r => r)
            .Must(r => HasNotExpired(r.ExpiryYear!.Value, r.ExpiryMonth!.Value))
            .WithName(nameof(PostPaymentRequest.ExpiryYear))
            .WithMessage("Card expiry date must be in the future")
            .When(r => r.ExpiryMonth is >= 1 and <= 12 && r.ExpiryYear is >= 1 and <= 9999);

        RuleFor(r => r.Currency)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Currency is required")
            .Length(3).WithMessage("Currency must be 3 characters")
            .Must(currency => SupportedCurrencies.Contains(currency))
            .WithMessage($"Currency must be one of {string.Join(", ", SupportedCurrencies)}");

        RuleFor(r => r.Amount)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Amount is required")
            .GreaterThan(0).WithMessage("Amount must be greater than zero");

        RuleFor(r => r.Cvv)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("CVV is required")
            .Matches("^[0-9]{3,4}$")
            .WithMessage("CVV must be 3-4 characters long and contain numeric characters only");
    }

    private static bool HasNotExpired(int year, int month)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return year > today.Year || (year == today.Year && month >= today.Month);
    }
}
