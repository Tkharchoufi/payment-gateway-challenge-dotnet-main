using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using PaymentGateway.Api.Clients;

namespace PaymentGateway.Api.Tests;

public class PaymentsControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task RetrievesAPaymentSuccessfully()
    {
        // Arrange
        var payment = StoredPayment();
        var paymentsRepository = new PaymentsRepository();
        paymentsRepository.Add(payment);

        var client = CreateClient(paymentsRepository);

        // Act
        var response = await client.GetAsync($"/api/Payments/{payment.Id}");
        var paymentResponse = await response.Content.ReadFromJsonAsync<GetPaymentResponse>(JsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(payment.Id, paymentResponse!.Id);
        Assert.Equal(PaymentStatus.Authorized, paymentResponse.Status);
        Assert.Equal("1234", paymentResponse.CardNumberLastFour);
        Assert.Equal(4, paymentResponse.ExpiryMonth);
        Assert.Equal(2030, paymentResponse.ExpiryYear);
        Assert.Equal("GBP", paymentResponse.Currency);
        Assert.Equal(100, paymentResponse.Amount);
    }

    [Fact]
    public async Task OnlyExposesTheLastFourCardDigits()
    {
        // Arrange
        var payment = StoredPayment();
        var paymentsRepository = new PaymentsRepository();
        paymentsRepository.Add(payment);

        var client = CreateClient(paymentsRepository);

        // Act
        var body = await client.GetStringAsync($"/api/Payments/{payment.Id}");

        // Assert
        Assert.DoesNotContain("2222405343241234", body);
        Assert.Contains("\"cardNumberLastFour\":\"1234\"", body);
        Assert.Contains("\"status\":\"Authorized\"", body);
    }

    [Fact]
    public async Task Returns404IfPaymentNotFound()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync($"/api/Payments/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreatesAnAuthorizedPayment()
    {
        // Arrange
        var client = CreateClient(acquiringBank: BankThatReturns(authorized: true).Object);

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", ValidPayload());
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.NotEqual(Guid.Empty, paymentResponse!.Id);
        Assert.Equal(PaymentStatus.Authorized, paymentResponse.Status);
        Assert.Equal("1234", paymentResponse.CardNumberLastFour);
        Assert.Equal("GBP", paymentResponse.Currency);
        Assert.Equal(100, paymentResponse.Amount);
    }

    [Fact]
    public async Task CreatesADeclinedPayment()
    {
        // Arrange
        var client = CreateClient(acquiringBank: BankThatReturns(authorized: false).Object);

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", ValidPayload());
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PaymentStatus.Declined, paymentResponse!.Status);
        Assert.NotEqual(Guid.Empty, paymentResponse.Id);
    }

    [Fact]
    public async Task RejectsAnInvalidRequestWithoutCallingTheBank()
    {
        // Arrange
        var acquiringBank = BankThatReturns(authorized: true);
        var client = CreateClient(acquiringBank: acquiringBank.Object);

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", ValidPayload() with { CardNumber = "nope" });
        var rejection = await response.Content.ReadFromJsonAsync<PostPaymentRejectedResponse>(JsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(PaymentStatus.Rejected, rejection!.Status);
        Assert.Contains(
            "Card number must be 14-19 characters long and contain numeric characters only",
            rejection.Errors);

        acquiringBank.Verify(
            bank => bank.AuthorizeAsync(It.IsAny<AcquiringBankRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReportsEveryValidationFailureInOneRejection()
    {
        // Arrange
        var client = CreateClient(acquiringBank: BankThatReturns(authorized: true).Object);

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", new { });
        var rejection = await response.Content.ReadFromJsonAsync<PostPaymentRejectedResponse>(JsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(6, rejection!.Errors.Count);
    }

    [Fact]
    public async Task ReturnsBadGatewayWhenTheBankGivesNoAnswer()
    {
        // Arrange
        var acquiringBank = new Mock<IAcquiringBankClient>();
        acquiringBank
            .Setup(bank => bank.AuthorizeAsync(It.IsAny<AcquiringBankRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AcquiringBankUnavailableException("The acquiring bank responded with 503."));

        var client = CreateClient(acquiringBank: acquiringBank.Object);

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", ValidPayload());

        // Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task APaymentCanBeRetrievedAfterItIsCreated()
    {
        // Arrange
        var client = CreateClient(acquiringBank: BankThatReturns(authorized: true).Object);

        // Act
        var createResponse = await client.PostAsJsonAsync("/api/Payments", ValidPayload());
        var created = await createResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);

        var retrieveResponse = await client.GetAsync($"/api/Payments/{created!.Id}");
        var retrieved = await retrieveResponse.Content.ReadFromJsonAsync<GetPaymentResponse>(JsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, retrieveResponse.StatusCode);
        Assert.Equal(created.Id, retrieved!.Id);
        Assert.Equal(created.Status, retrieved.Status);
        Assert.Equal(created.CardNumberLastFour, retrieved.CardNumberLastFour);
        Assert.Equal(created.ExpiryMonth, retrieved.ExpiryMonth);
        Assert.Equal(created.ExpiryYear, retrieved.ExpiryYear);
        Assert.Equal(created.Currency, retrieved.Currency);
        Assert.Equal(created.Amount, retrieved.Amount);
    }

    [Fact]
    public async Task NeverEchoesTheFullCardNumberBackToTheMerchant()
    {
        // Arrange
        var client = CreateClient(acquiringBank: BankThatReturns(authorized: true).Object);

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", ValidPayload());
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.DoesNotContain("2222405343241234", body);
        Assert.Contains("\"cardNumberLastFour\":\"1234\"", body);
    }

    private static Mock<IAcquiringBankClient> BankThatReturns(bool authorized)
    {
        var acquiringBank = new Mock<IAcquiringBankClient>();

        acquiringBank
            .Setup(bank => bank.AuthorizeAsync(It.IsAny<AcquiringBankRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcquiringBankResponse
            {
                Authorized = authorized,
                AuthorizationCode = authorized ? "0bb07405-6d44-4b50-a14f-7ae0beff13ad" : ""
            });

        return acquiringBank;
    }

    private static HttpClient CreateClient(
        IPaymentsRepository? paymentsRepository = null,
        IAcquiringBankClient? acquiringBank = null)
    {
        return new WebApplicationFactory<PaymentsController>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IPaymentsRepository>();
                    services.AddSingleton(paymentsRepository ?? new PaymentsRepository());

                    services.RemoveAll<IAcquiringBankClient>();
                    services.AddSingleton(
                        acquiringBank ?? new Mock<IAcquiringBankClient>(MockBehavior.Strict).Object);
                }))
            .CreateClient();
    }

    private static Payment StoredPayment() => new()
    {
        Id = Guid.NewGuid(),
        Status = PaymentStatus.Authorized,
        CardNumberLastFour = "1234",
        ExpiryMonth = 4,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 100
    };

    private static Payload ValidPayload() => new(
        CardNumber: "2222405343241234",
        ExpiryMonth: 4,
        ExpiryYear: DateTime.UtcNow.Year + 1,
        Currency: "GBP",
        Amount: 100,
        Cvv: "123");

    private record Payload(
        string CardNumber,
        int ExpiryMonth,
        int ExpiryYear,
        string Currency,
        int Amount,
        string Cvv);
}
