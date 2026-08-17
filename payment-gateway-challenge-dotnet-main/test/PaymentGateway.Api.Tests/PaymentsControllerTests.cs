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
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            ExpiryYear = 2030,
            ExpiryMonth = 4,
            Amount = 100,
            Status = PaymentStatus.Authorized,
            CardNumberLastFour = "1234",
            Currency = "GBP"
        };

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
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Status = PaymentStatus.Authorized,
            CardNumberLastFour = "8877",
            ExpiryMonth = 4,
            ExpiryYear = 2030,
            Currency = "GBP",
            Amount = 100
        };

        var paymentsRepository = new PaymentsRepository();
        paymentsRepository.Add(payment);

        var client = CreateClient(paymentsRepository);

        // Act
        var body = await client.GetStringAsync($"/api/Payments/{payment.Id}");

        // Assert
        Assert.DoesNotContain("2222405343248877", body);
        Assert.Contains("\"cardNumberLastFour\":\"8877\"", body);
        Assert.Contains("\"status\":\"Authorized\"", body);
    }

    [Fact]
    public async Task Returns404IfPaymentNotFound()
    {
        // Arrange
        var client = CreateClient(new PaymentsRepository());
        
        // Act
        var response = await client.GetAsync($"/api/Payments/{Guid.NewGuid()}");
        
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }


    private static HttpClient CreateClient(PaymentsRepository paymentsRepository)
    {
        return new WebApplicationFactory<PaymentsController>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton(paymentsRepository)))
            .CreateClient();
    }
}