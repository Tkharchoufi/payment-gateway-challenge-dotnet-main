using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class PaymentsRepository:IPaymentsRepository
{
    private readonly List<Payment> Payments = new();
    
    public void Add(Payment payment)
    {
        Payments.Add(payment);
    }

    public Payment? Get(Guid id)
    {
        return Payments.FirstOrDefault(p => p.Id == id);
    }
}