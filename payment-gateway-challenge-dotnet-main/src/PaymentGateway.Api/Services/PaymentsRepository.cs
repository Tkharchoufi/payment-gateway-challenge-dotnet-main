using System.Collections.Concurrent;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class PaymentsRepository:IPaymentsRepository
{
    private readonly ConcurrentDictionary<Guid, Payment> Payments = new();
    
    public void Add(Payment payment)
    {
        Payments[payment.Id] = payment;
    }

    public Payment? Get(Guid id)
    {
        return Payments.TryGetValue(id, out var payment) ? payment : null;
    }
}