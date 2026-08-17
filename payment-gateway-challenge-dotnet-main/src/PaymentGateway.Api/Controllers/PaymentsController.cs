using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : Controller
{
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly PaymentsService _paymentsService;
    private readonly IValidator<PostPaymentRequest> _validator;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentsRepository paymentsRepository,
        PaymentsService paymentsService,
        IValidator<PostPaymentRequest> validator,
        ILogger<PaymentsController> logger)
    {
        _paymentsRepository = paymentsRepository;
        _paymentsService = paymentsService;
        _validator = validator;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType<PostPaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<PostPaymentRejectedResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> PostPayment(
        PostPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BadRequest(new PostPaymentRejectedResponse
            {
                Status = PaymentStatus.Rejected,
                Errors = validationResult.Errors.Select(error => error.ErrorMessage).ToArray()
            });
        }

        Payment payment;

        try
        {
            payment = await _paymentsService.ProcessAsync(request, cancellationToken);
        }
        catch (AcquiringBankUnavailableException exception)
        {
            _logger.LogError(exception, "The acquiring bank did not return a payment outcome.");

            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The acquiring bank did not return a payment outcome.",
                detail: "No payment was recorded. Retrying may result in a duplicate authorization.");
        }

        return Ok(new PostPaymentResponse
        {
            Id = payment.Id,
            Status = payment.Status,
            CardNumberLastFour = payment.CardNumberLastFour,
            ExpiryMonth = payment.ExpiryMonth,
            ExpiryYear = payment.ExpiryYear,
            Currency = payment.Currency,
            Amount = payment.Amount
        });
    }


    [HttpGet("{id:guid}")]
    [ProducesResponseType<GetPaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<GetPaymentResponse?> GetPayment(Guid id)
    {
        var payment = _paymentsRepository.Get(id);

        return payment is null 
            ? NotFound() 
            : new OkObjectResult(new GetPaymentResponse
            {
                Id = payment.Id,
                Status = payment.Status,
                CardNumberLastFour = payment.CardNumberLastFour,
                ExpiryMonth = payment.ExpiryMonth,
                ExpiryYear = payment.ExpiryYear,
                Currency = payment.Currency,
                Amount = payment.Amount
            });
    }
}