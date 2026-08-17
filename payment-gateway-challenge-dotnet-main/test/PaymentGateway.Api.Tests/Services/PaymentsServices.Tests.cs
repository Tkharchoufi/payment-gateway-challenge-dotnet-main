using Moq;

using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests.Services;

public class PaymentsServiceTests
{
    private readonly Mock<IAcquiringBankClient> _acquiringBank = new();
    private readonly PaymentsRepository _repository = new();
    private readonly PaymentsService _service;

    public PaymentsServiceTests()
    {
        _service = new PaymentsService(_acquiringBank.Object, _repository);
    }

    [Fact]
    public async Task SendsTheRequestInTheFormatTheBankExpects()
    {
        var sent = CaptureBankRequest(Authorized());

        await _service.ProcessAsync(ValidRequest());

        Assert.NotNull(sent.Value);
        Assert.Equal("2222405343248877", sent.Value!.CardNumber);
        Assert.Equal("04/2030", sent.Value.ExpiryDate);
        Assert.Equal("GBP", sent.Value.Currency);
        Assert.Equal(100, sent.Value.Amount);
        Assert.Equal("123", sent.Value.Cvv);
    }

    [Theory]
    [InlineData(1, 2030, "01/2030")]
    [InlineData(9, 2030, "09/2030")]
    [InlineData(10, 2030, "10/2030")]
    [InlineData(12, 2031, "12/2031")]
    public async Task ZeroPadsTheExpiryMonthForTheBank(int month, int year, string expected)
    {
        var sent = CaptureBankRequest(Authorized());

        await _service.ProcessAsync(ValidRequest() with { ExpiryMonth = month, ExpiryYear = year });

        Assert.Equal(expected, sent.Value!.ExpiryDate);
    }

    [Fact]
    public async Task RecordsAnAuthorizedPayment()
    {
        StubBank(Authorized());

        var payment = await _service.ProcessAsync(ValidRequest());

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal(payment, _repository.Get(payment.Id));
    }

    [Fact]
    public async Task RecordsADeclinedPayment()
    {
        StubBank(new AcquiringBankResponse { Authorized = false, AuthorizationCode = "" });

        var payment = await _service.ProcessAsync(ValidRequest());

        Assert.Equal(PaymentStatus.Declined, payment.Status);
        Assert.Equal(payment, _repository.Get(payment.Id));
    }

    [Fact]
    public async Task RecordsOnlyTheLastFourCardDigits()
    {
        StubBank(Authorized());

        var payment = await _service.ProcessAsync(ValidRequest());

        Assert.Equal("8877", payment.CardNumberLastFour);
    }

    [Fact]
    public async Task CarriesTheRequestDetailsOntoTheRecordedPayment()
    {
        StubBank(Authorized());

        var payment = await _service.ProcessAsync(ValidRequest());

        Assert.Equal(4, payment.ExpiryMonth);
        Assert.Equal(2030, payment.ExpiryYear);
        Assert.Equal("GBP", payment.Currency);
        Assert.Equal(100, payment.Amount);
    }

    [Fact]
    public async Task RecordsNothingWhenTheBankGivesNoAnswer()
    {
        var repository = new Mock<IPaymentsRepository>();
        var service = new PaymentsService(_acquiringBank.Object, repository.Object);

        _acquiringBank
            .Setup(bank => bank.AuthorizeAsync(It.IsAny<AcquiringBankRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AcquiringBankUnavailableException("bank is down"));

        await Assert.ThrowsAsync<AcquiringBankUnavailableException>(() => service.ProcessAsync(ValidRequest()));

        repository.Verify(r => r.Add(It.IsAny<Payment>()), Times.Never);
    }

    [Fact]
    public async Task GivesEveryPaymentItsOwnId()
    {
        StubBank(Authorized());

        var first = await _service.ProcessAsync(ValidRequest());
        var second = await _service.ProcessAsync(ValidRequest());

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotNull(_repository.Get(first.Id));
        Assert.NotNull(_repository.Get(second.Id));
    }

    private void StubBank(AcquiringBankResponse response)
    {
        _acquiringBank
            .Setup(bank => bank.AuthorizeAsync(It.IsAny<AcquiringBankRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    private Captured<AcquiringBankRequest> CaptureBankRequest(AcquiringBankResponse response)
    {
        var captured = new Captured<AcquiringBankRequest>();

        _acquiringBank
            .Setup(bank => bank.AuthorizeAsync(It.IsAny<AcquiringBankRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AcquiringBankRequest, CancellationToken>((request, _) => captured.Value = request)
            .ReturnsAsync(response);

        return captured;
    }

    private sealed class Captured<T>
    {
        public T? Value { get; set; }
    }

    private static AcquiringBankResponse Authorized() => new()
    {
        Authorized = true,
        AuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad"
    };

    private static PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = "2222405343248877",
        ExpiryMonth = 4,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };
}
