using FluentValidation.TestHelper;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Validation;

namespace PaymentGateway.Api.Tests.Validation;

public class PostPaymentRequestValidatorTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly PostPaymentRequestValidator _validator = new();

    [Fact]
    public void AcceptsAValidRequest()
    {
        _validator.TestValidate(ValidRequest()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null, "Card number is required")]
    [InlineData("", "Card number is required")]
    [InlineData("1234567890123", "Card number must be 14-19 characters long and contain numeric characters only")]
    [InlineData("12345678901234567890", "Card number must be 14-19 characters long and contain numeric characters only")]
    [InlineData("222240534324887a", "Card number must be 14-19 characters long and contain numeric characters only")]
    [InlineData("2222 4053 4324 8877", "Card number must be 14-19 characters long and contain numeric characters only")]
    [InlineData("-222405343248877", "Card number must be 14-19 characters long and contain numeric characters only")]
    public void RejectsAnInvalidCardNumber(string? cardNumber, string expectedMessage)
    {
        var request = ValidRequest() with { CardNumber = cardNumber };

        _validator.TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.CardNumber)
            .WithErrorMessage(expectedMessage);
    }

    [Theory]
    [InlineData("12345678901234")]
    [InlineData("1234567890123456789")]
    [InlineData("00000000000001")]
    public void AcceptsACardNumberOnTheLengthBoundaries(string cardNumber)
    {
        var request = ValidRequest() with { CardNumber = cardNumber };

        _validator.TestValidate(request).ShouldNotHaveValidationErrorFor(r => r.CardNumber);
    }

    [Theory]
    [InlineData(null, "Expiry month is required")]
    [InlineData(0, "Expiry month must be between 1 and 12")]
    [InlineData(13, "Expiry month must be between 1 and 12")]
    [InlineData(-1, "Expiry month must be between 1 and 12")]
    public void RejectsAnInvalidExpiryMonth(int? expiryMonth, string expectedMessage)
    {
        var request = ValidRequest() with { ExpiryMonth = expiryMonth };

        _validator.TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.ExpiryMonth)
            .WithErrorMessage(expectedMessage);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void AcceptsAnExpiryMonthOnTheBoundaries(int expiryMonth)
    {
        var request = ValidRequest() with { ExpiryMonth = expiryMonth };

        _validator.TestValidate(request).ShouldNotHaveValidationErrorFor(r => r.ExpiryMonth);
    }

    [Fact]
    public void RejectsAMissingExpiryYear()
    {
        var request = ValidRequest() with { ExpiryYear = null };

        _validator.TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.ExpiryYear)
            .WithErrorMessage("Expiry year is required");
    }

    public static TheoryData<int, int> ExpiryDatesInThePast()
    {
        var lastMonth = Today.AddMonths(-1);
        var lastYear = Today.AddYears(-1);

        return new TheoryData<int, int>
        {
            { lastMonth.Year, lastMonth.Month },
            { lastYear.Year, lastYear.Month },
            { 2000, 1 }
        };
    }

    public static TheoryData<int, int> ExpiryDatesThatHaveNotPassed()
    {
        var nextMonth = Today.AddMonths(1);
        var nextYear = Today.AddYears(1);

        return new TheoryData<int, int>
        {
            { Today.Year, Today.Month }, 
            { nextMonth.Year, nextMonth.Month },
            { nextYear.Year, nextYear.Month }
        };
    }

    [Theory]
    [MemberData(nameof(ExpiryDatesInThePast))]
    public void RejectsAnExpiryDateInThePast(int expiryYear, int expiryMonth)
    {
        var request = ValidRequest() with { ExpiryYear = expiryYear, ExpiryMonth = expiryMonth };

        _validator.TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.ExpiryYear)
            .WithErrorMessage("Card expiry date must be in the future");
    }

    [Theory]
    [MemberData(nameof(ExpiryDatesThatHaveNotPassed))]
    public void AcceptsAnExpiryDateThatHasNotPassed(int expiryYear, int expiryMonth)
    {
        var request = ValidRequest() with { ExpiryYear = expiryYear, ExpiryMonth = expiryMonth };

        _validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void DoesNotReportExpiryWhenTheMonthIsAlreadyOutOfRange()
    {
        var request = ValidRequest() with { ExpiryMonth = 99, ExpiryYear = 2020 };

        var result = _validator.TestValidate(request);

        Assert.Single(result.Errors);
        Assert.Equal("Expiry month must be between 1 and 12", result.Errors[0].ErrorMessage);
    }

    [Theory]
    [InlineData(null, "Currency is required")]
    [InlineData("", "Currency is required")]
    [InlineData("GB", "Currency must be 3 characters")]
    [InlineData("GBPP", "Currency must be 3 characters")]
    [InlineData("JPY", "Currency must be one of GBP, USD, EUR")]
    [InlineData("gbp", "Currency must be one of GBP, USD, EUR")]
    public void RejectsAnUnsupportedCurrency(string? currency, string expectedMessage)
    {
        var request = ValidRequest() with { Currency = currency };

        _validator.TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.Currency)
            .WithErrorMessage(expectedMessage);
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("USD")]
    [InlineData("EUR")]
    public void AcceptsEverySupportedCurrency(string currency)
    {
        var request = ValidRequest() with { Currency = currency };

        _validator.TestValidate(request).ShouldNotHaveValidationErrorFor(r => r.Currency);
    }

    [Theory]
    [InlineData(null, "Amount is required")]
    [InlineData(0, "Amount must be greater than zero")]
    [InlineData(-1, "Amount must be greater than zero")]
    public void RejectsAnInvalidAmount(int? amount, string expectedMessage)
    {
        var request = ValidRequest() with { Amount = amount };

        _validator.TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.Amount)
            .WithErrorMessage(expectedMessage);
    }

    [Fact]
    public void AcceptsTheSmallestPayableAmount()
    {
        var request = ValidRequest() with { Amount = 1 };

        _validator.TestValidate(request).ShouldNotHaveValidationErrorFor(r => r.Amount);
    }

    [Theory]
    [InlineData(null, "CVV is required")]
    [InlineData("", "CVV is required")]
    [InlineData("12", "CVV must be 3-4 characters long and contain numeric characters only")]
    [InlineData("12345", "CVV must be 3-4 characters long and contain numeric characters only")]
    [InlineData("12a", "CVV must be 3-4 characters long and contain numeric characters only")]
    public void RejectsAnInvalidCvv(string? cvv, string expectedMessage)
    {
        var request = ValidRequest() with { Cvv = cvv };

        _validator.TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.Cvv)
            .WithErrorMessage(expectedMessage);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234")]
    [InlineData("0000")]
    public void AcceptsACvvOnTheLengthBoundaries(string cvv)
    {
        var request = ValidRequest() with { Cvv = cvv };

        _validator.TestValidate(request).ShouldNotHaveValidationErrorFor(r => r.Cvv);
    }

    [Fact]
    public void ReportsEveryInvalidFieldAtOnce()
    {
        var request = new PostPaymentRequest();

        var result = _validator.TestValidate(request);

        Assert.Equal(6, result.Errors.Count);
    }

    private static PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = "2222405343248877",
        ExpiryMonth = Today.Month,
        ExpiryYear = Today.Year + 1,
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };
}
