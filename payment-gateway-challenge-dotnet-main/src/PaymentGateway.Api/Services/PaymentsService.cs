using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public class PaymentsService
{
    private readonly IAcquiringBankClient _acquiringBankClient;
    private readonly IPaymentsRepository _paymentsRepository;

    public PaymentsService(IAcquiringBankClient acquiringBankClient, IPaymentsRepository paymentsRepository)
    {
        _acquiringBankClient = acquiringBankClient;
        _paymentsRepository = paymentsRepository;
    }

    public async Task<Payment> ProcessAsync(PostPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var bankRequest = new AcquiringBankRequest
        {
            CardNumber = request.CardNumber!,
            ExpiryDate = $"{request.ExpiryMonth!.Value:D2}/{request.ExpiryYear!.Value}",
            Currency = request.Currency!,
            Amount = request.Amount!.Value,
            Cvv = request.Cvv!
        };

        var bankResponse = await _acquiringBankClient.AuthorizeAsync(bankRequest, cancellationToken);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Status = bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined,
            CardNumberLastFour = request.CardNumber![^4..],
            ExpiryMonth = request.ExpiryMonth.Value,
            ExpiryYear = request.ExpiryYear.Value,
            Currency = request.Currency!,
            Amount = request.Amount.Value
        };

        _paymentsRepository.Add(payment);

        return payment;
    }
}
