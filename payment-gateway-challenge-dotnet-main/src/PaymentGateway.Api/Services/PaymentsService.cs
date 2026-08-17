using System.Diagnostics;

using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public class PaymentsService
{
    private readonly IAcquiringBankClient _acquiringBankClient;
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly ILogger<PaymentsService> _logger;

    public PaymentsService(IAcquiringBankClient acquiringBankClient, IPaymentsRepository paymentsRepository, ILogger<PaymentsService> logger)
    {
        _acquiringBankClient = acquiringBankClient;
        _paymentsRepository = paymentsRepository;
        _logger = logger;
    }

    public async Task<Payment> ProcessAsync(PostPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var paymentId = Guid.NewGuid();
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["PaymentId"] = paymentId
        });
        var bankRequest = new AcquiringBankRequest
        {
            CardNumber = request.CardNumber!,
            ExpiryDate = $"{request.ExpiryMonth!.Value:D2}/{request.ExpiryYear!.Value}",
            Currency = request.Currency!,
            Amount = request.Amount!.Value,
            Cvv = request.Cvv!
        };

        _logger.LogInformation(
            "Authorizing {Amount} {Currency} with the acquiring bank.",
            bankRequest.Amount,
            bankRequest.Currency);

        var timer = Stopwatch.StartNew();
        AcquiringBankResponse bankResponse;

        try
        {
            bankResponse = await _acquiringBankClient.AuthorizeAsync(bankRequest, cancellationToken);
        }
        catch (AcquiringBankUnavailableException exception)
        {
            _logger.LogError(
                exception,
                "The acquiring bank gave no answer after {ElapsedMilliseconds}ms. No payment recorded.",
                timer.ElapsedMilliseconds);

            throw;
        }


        var payment = new Payment
        {
            Id = paymentId,
            Status = bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined,
            CardNumberLastFour = request.CardNumber![^4..],
            ExpiryMonth = request.ExpiryMonth.Value,
            ExpiryYear = request.ExpiryYear.Value,
            Currency = request.Currency!,
            Amount = request.Amount.Value
        };

        _paymentsRepository.Add(payment);

        _logger.LogInformation(
            "Payment {PaymentStatus} by the acquiring bank in {ElapsedMilliseconds}ms.",
            payment.Status,
            timer.ElapsedMilliseconds);
            
        return payment;
    }
}
