namespace PaymentGateway.Api.Models.Requests;

public record PostPaymentRequest
{
    public string? CardNumber { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? Currency { get; set; }
    public int? Amount { get; set; }
    public string? Cvv { get; set; }
}