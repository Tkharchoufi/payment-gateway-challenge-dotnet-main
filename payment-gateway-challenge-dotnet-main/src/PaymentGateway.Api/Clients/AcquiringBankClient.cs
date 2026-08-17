using System.Text.Json;

namespace PaymentGateway.Api.Clients;

public class AcquiringBankClient : IAcquiringBankClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;

    public AcquiringBankClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AcquiringBankResponse> AuthorizeAsync(
        AcquiringBankRequest request,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.PostAsJsonAsync("/payments", request, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new AcquiringBankUnavailableException("The acquiring bank could not be reached.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AcquiringBankUnavailableException("The acquiring bank did not respond in time.", exception);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new AcquiringBankUnavailableException(
                $"The acquiring bank responded with {(int)response.StatusCode}.");
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<AcquiringBankResponse>(JsonOptions, cancellationToken)
                   ?? throw new AcquiringBankUnavailableException("The acquiring bank returned an empty body.");
        }
        catch (JsonException exception)
        {
            throw new AcquiringBankUnavailableException("The acquiring bank returned a malformed body.", exception);
        }
    }
}