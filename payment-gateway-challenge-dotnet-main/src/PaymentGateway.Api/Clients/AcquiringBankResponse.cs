namespace PaymentGateway.Api.Clients;

public record AcquiringBankResponse
{
    public required bool Authorized { get; init; }

    public string? AuthorizationCode { get; init; }
}