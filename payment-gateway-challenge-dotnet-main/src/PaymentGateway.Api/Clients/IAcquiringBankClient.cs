namespace PaymentGateway.Api.Clients;


public interface IAcquiringBankClient
{
    Task<AcquiringBankResponse> AuthorizeAsync(
        AcquiringBankRequest request,
        CancellationToken cancellationToken = default);
}