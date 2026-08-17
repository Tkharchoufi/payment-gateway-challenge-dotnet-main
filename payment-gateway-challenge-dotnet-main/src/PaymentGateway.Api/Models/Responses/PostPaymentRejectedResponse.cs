namespace PaymentGateway.Api.Models.Responses;

public record PostPaymentRejectedResponse
{
    public required PaymentStatus Status { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
}