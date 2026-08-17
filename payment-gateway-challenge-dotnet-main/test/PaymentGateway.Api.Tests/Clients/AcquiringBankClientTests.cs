using System.Net;
using System.Text;

using Moq;
using Moq.Protected;

using PaymentGateway.Api.Clients;

namespace PaymentGateway.Api.Tests.Clients;

public class AcquiringBankClientTests
{
    private static readonly AcquiringBankRequest Request = new()
    {
        CardNumber = "2222405343248877",
        ExpiryDate = "04/2030",
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };

    [Fact]
    public async Task PostsSnakeCaseJsonToThePaymentsRoute()
    {
        HttpRequestMessage? sent = null;
        string? sentBody = null;

        var client = CreateClient(
            HttpStatusCode.OK,
            """{"authorized":true,"authorization_code":"0bb07405-6d44-4b50-a14f-7ae0beff13ad"}""",
            onSend: request =>
            {
                sent = request;
                sentBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            });

        await client.AuthorizeAsync(Request);

        Assert.NotNull(sent);
        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal("http://bank.test/payments", sent.RequestUri!.ToString());
        Assert.Equal(
            """{"card_number":"2222405343248877","expiry_date":"04/2030","currency":"GBP","amount":100,"cvv":"123"}""",
            sentBody);
    }

    [Fact]
    public async Task ReturnsTheAuthorizationCodeWhenTheBankAuthorizes()
    {
        var client = CreateClient(
            HttpStatusCode.OK,
            """{"authorized":true,"authorization_code":"0bb07405-6d44-4b50-a14f-7ae0beff13ad"}""");

        var response = await client.AuthorizeAsync(Request);

        Assert.True(response.Authorized);
        Assert.Equal("0bb07405-6d44-4b50-a14f-7ae0beff13ad", response.AuthorizationCode);
    }

    [Fact]
    public async Task ReturnsADeclineWhenTheBankDoesNotAuthorize()
    {
        var client = CreateClient(HttpStatusCode.OK, """{"authorized":false,"authorization_code":""}""");

        var response = await client.AuthorizeAsync(Request);

        Assert.False(response.Authorized);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ThrowsWhenTheBankDoesNotReturnSuccess(HttpStatusCode statusCode)
    {
        var client = CreateClient(statusCode, "{}");

        var exception = await Assert.ThrowsAsync<AcquiringBankUnavailableException>(
            () => client.AuthorizeAsync(Request));

        Assert.Contains(((int)statusCode).ToString(), exception.Message);
    }

    [Fact]
    public async Task ThrowsWhenTheBankCannotBeReached()
    {
        var client = CreateClientThatThrows(new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<AcquiringBankUnavailableException>(() => client.AuthorizeAsync(Request));
    }

    [Fact]
    public async Task ThrowsWhenTheBankDoesNotRespondInTime()
    {
        var client = CreateClientThatThrows(new TaskCanceledException("timed out"));

        var exception = await Assert.ThrowsAsync<AcquiringBankUnavailableException>(
            () => client.AuthorizeAsync(Request));

        Assert.Contains("in time", exception.Message);
    }

    [Fact]
    public async Task DoesNotDisguiseCallerCancellationAsABankFailure()
    {
        var client = CreateClientThatThrows(new TaskCanceledException("cancelled"));
        var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.AuthorizeAsync(Request, cancelled.Token));
    }

    [Fact]
    public async Task ThrowsWhenTheBankReturnsABodyThatCannotBeRead()
    {
        var client = CreateClient(HttpStatusCode.OK, "not json at all");

        await Assert.ThrowsAsync<AcquiringBankUnavailableException>(() => client.AuthorizeAsync(Request));
    }

    [Fact]
    public async Task ThrowsWhenTheBankOmitsTheAuthorizationDecision()
    {
        var client = CreateClient(HttpStatusCode.OK, """{"authorization_code":"abc"}""");

        await Assert.ThrowsAsync<AcquiringBankUnavailableException>(() => client.AuthorizeAsync(Request));
    }

    private static AcquiringBankClient CreateClient(
        HttpStatusCode statusCode,
        string body,
        Action<HttpRequestMessage>? onSend = null)
    {
        var handler = new Mock<HttpMessageHandler>();

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => onSend?.Invoke(request))
            .ReturnsAsync(() => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        return BuildClient(handler);
    }

    private static AcquiringBankClient CreateClientThatThrows(Exception exception)
    {
        var handler = new Mock<HttpMessageHandler>();

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(exception);

        return BuildClient(handler);
    }

    private static AcquiringBankClient BuildClient(Mock<HttpMessageHandler> handler)
    {
        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("http://bank.test")
        };

        return new AcquiringBankClient(httpClient);
    }
}
